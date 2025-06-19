using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Net.Security;
using Server.Logger;
using Server.Core.Common;

namespace Server.Core.Certification
{
    public class SSLManager
    {
        private ILogger _logger;

        public SSLManager(ILogger logger)
        {
            _logger = logger;
        }

        public static X509Certificate2 LoadOrCreateCertificate()
        {
            try
            {
                // 尝试从文件加载证书
                return new X509Certificate2("server.pfx", "password");
            }
            catch
            {
                // 如果证书不存在，则创建自签名证书
                Console.WriteLine("未找到证书，正在创建自签名证书...");
                var certificate = CreateSelfSignedCertificate("localhost");

                // 保存证书以便将来使用
                SaveCertificate(certificate);
                return certificate;
            }
        }
        private static X509Certificate2 CreateSelfSignedCertificate(string subjectName)
        {
            using var rsa = RSA.Create(2048);

            // 创建证书请求
            var certificateRequest = new CertificateRequest(
                new X500DistinguishedName($"CN={subjectName}"),
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // 添加基本约束
            certificateRequest.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: false,
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: true));

            // 添加密钥用法
            certificateRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    critical: true));

            // 添加增强密钥用法 (EKU)
            certificateRequest.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },  // 服务器认证
                    critical: false));

            // 添加主题替代名称 (SAN)
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(subjectName);
            certificateRequest.CertificateExtensions.Add(sanBuilder.Build());

            // 生成自签名证书
            var certificate = certificateRequest.CreateSelfSigned(
                notBefore: DateTimeOffset.UtcNow.AddDays(-1),
                notAfter: DateTimeOffset.UtcNow.AddDays(365));

            // 创建可导出私钥的证书
            return new X509Certificate2(
                certificate.Export(X509ContentType.Pfx, "password"),
                "password",
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }
        private static void SaveCertificate(X509Certificate2 certificate)
        {
            File.WriteAllBytes("server.pfx", certificate.Export(X509ContentType.Pfx, "password"));
            ExportCertificatePublicKey(certificate);
            Console.WriteLine("自签名证书已创建并保存为 server.pfx");
        }
        private static void ExportCertificatePublicKey(X509Certificate2 certificate)
        {
            File.WriteAllBytes("server.cer", certificate.Export(X509ContentType.Cert));
            Console.WriteLine("证书公钥已导出为 server.cer");
        }

        // 自定义客户端证书验证回调
        public bool ValidateClientCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            // 基础验证：证书不能为空
            if (certificate == null)
            {
                _logger.LogCritical("Client certificate is null - Authentication failed");
                return false;
            }

            _logger.LogInformation($"Client certificate received: {certificate.Subject}");
            _logger.LogDebug($"Certificate details: Issuer={certificate.Issuer}, Serial={certificate.GetSerialNumberString()}");

            // 开发环境配置：宽松验证
            bool isDevelopment = IsDevelopmentEnvironment();
            _logger.LogTrace($"Environment mode: {(isDevelopment ? "Development" : "Production")}");

            // 证书指纹验证
            bool thumbprintValid = ValidateCertificateThumbprint(certificate);
            _logger.LogDebug($"Certificate thumbprint validation: {(thumbprintValid ? "Passed" : "Failed")}");

            if (!thumbprintValid)
            {
                _logger.LogWarning($"Certificate thumbprint mismatch: {certificate.GetCertHashString()}");
                if (!isDevelopment)
                {
                    _logger.LogError("Rejected in non-development environment due to thumbprint mismatch");
                    return false;
                }
                _logger.LogWarning("Allowing certificate with invalid thumbprint in development environment");
            }

            // 处理证书链错误
            if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            {
                if (isDevelopment)
                {
                    _logger.LogWarning("Accepting untrusted certificate chain in development environment");
                    return true; // 开发环境直接通过
                }

                // 生产环境下，使用加载的根证书和系统默认根证书验证
                bool customChainValid = ValidateCertificateChainWithCustomRoots(certificate);

                if (!customChainValid)
                {
                    _logger.LogCritical("Certificate chain validation failed with both custom and system roots - Authentication failed");
                    return false;
                }

                _logger.LogInformation("Certificate chain validation passed using custom and system roots");
            }

            // 处理其他证书错误
            if ((sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            {
                _logger.LogError($"Certificate validation errors: {sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors}");
                return false;
            }

            // 证书主体白名单验证
            string[] allowedSubjects = { "CN=trusted-client", "CN=api-client" };
            bool subjectAllowed = allowedSubjects.Contains(certificate.Subject);

            _logger.LogDebug($"Certificate subject validation: {(subjectAllowed ? "Allowed" : "Rejected")} - {certificate.Subject}");
            if (!subjectAllowed)
            {
                _logger.LogWarning($"Certificate subject not in allowed list: {certificate.Subject}");

                if (isDevelopment)
                {
                    _logger.LogWarning("Allowing certificate with unrecognized subject in development environment");
                    return true; // 开发环境直接通过
                }

                return false;
            }

            // 证书有效期验证 - 开发环境允许1天内的过期
            DateTime now = DateTime.UtcNow;
            if (DateTime.TryParse(certificate.GetExpirationDateString(), out DateTime expiryDate) &&
                expiryDate < now)
            {
                // 计算过期时间差
                TimeSpan expiredBy = now - expiryDate;

                if (isDevelopment && expiredBy.TotalDays <= 1)
                {
                    _logger.LogWarning($"Certificate expired {expiredBy:g} ago, but allowing in development environment");
                    return true; // 开发环境允许1天内过期
                }

                _logger.LogError($"Certificate has expired: {expiryDate:u} (Current: {now:u})");
                return false;
            }

            _logger.LogInformation("Client certificate validation passed successfully");
            return true;
        }

        // 使用自定义根证书和系统默认根证书验证证书链
        private bool ValidateCertificateChainWithCustomRoots(X509Certificate certificate)
        {
            using var x509Cert = new X509Certificate2(certificate);
            using var customChain = new X509Chain();

            // 配置证书链验证参数
            customChain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            customChain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag; // 严格验证
            customChain.ChainPolicy.VerificationTime = DateTime.Now;

            // 加载自定义根证书
            X509Certificate2Collection customRoots = LoadCustomRootCertificates();

            // 将自定义根证书添加到证书链的额外存储中
            foreach (var rootCert in customRoots)
            {
                customChain.ChainPolicy.ExtraStore.Add(rootCert);
            }

            // 构建证书链
            bool isValid = customChain.Build(x509Cert);

            // 记录证书链状态
            if (!isValid)
            {
                _logger.LogWarning("Certificate chain validation failed with custom roots");
                foreach (var status in customChain.ChainStatus)
                {
                    _logger.LogDebug($"Custom chain status: {status.Status} - {status.StatusInformation}");
                }

                // 尝试仅使用系统默认根证书验证
                using var systemChain = new X509Chain();
                systemChain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                systemChain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                systemChain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag; // 严格验证
                systemChain.ChainPolicy.VerificationTime = DateTime.Now;

                bool systemValid = systemChain.Build(x509Cert);

                if (!systemValid)
                {
                    _logger.LogWarning("Certificate chain validation failed with system roots");
                    foreach (var status in systemChain.ChainStatus)
                    {
                        _logger.LogDebug($"System chain status: {status.Status} - {status.StatusInformation}");
                    }
                    return false;
                }

                _logger.LogInformation("Certificate chain validation passed using system roots only");
            }

            return true;
        }

        // 加载自定义根证书
        private X509Certificate2Collection LoadCustomRootCertificates()
        {
            var customRoots = new X509Certificate2Collection();

            try
            {
                // 从配置文件、密钥库或其他安全存储加载自定义根证书
                // 这里使用示例证书数据，实际应用中应替换为实际加载逻辑
                string[] certificateFiles = GetCustomRootCertificateFiles();

                foreach (var certFile in certificateFiles)
                {
                    if (File.Exists(certFile))
                    {
                        var cert = new X509Certificate2(certFile);
                        customRoots.Add(cert);
                        _logger.LogDebug($"Loaded custom root certificate: {cert.Subject}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading custom root certificates: {ex.Message}");
            }

            return customRoots;
        }

        // 获取自定义根证书文件路径
        private string[] GetCustomRootCertificateFiles()
        {
            // 实际应用中应从配置或环境变量获取
            return new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Certificates", "CustomRoot1.crt"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Certificates", "CustomRoot2.crt")
            };
        }

        // 证书指纹验证方法
        private bool ValidateCertificateThumbprint(X509Certificate certificate)
        {
            using var x509Cert = new X509Certificate2(certificate);
            string certificateHash = BitConverter.ToString(x509Cert.GetCertHash()).Replace("-", "").ToUpperInvariant();

            // 从配置加载允许的指纹列表
            var allowedThumbprints = LoadAllowedThumbprints();

            _logger.LogDebug($"Verifying certificate thumbprint: {certificateHash}");

            bool isValid = allowedThumbprints.Contains(certificateHash);
            if (!isValid)
            {
                _logger.LogDebug($"Thumbprint not in allowed list. Allowed: {string.Join(", ", allowedThumbprints)}");
            }

            return isValid;
        }

        // 从配置加载允许的指纹列表
        private HashSet<string> LoadAllowedThumbprints()
        {
            // 实际应用中应从配置文件或安全存储加载
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "1234567890ABCDEF1234567890ABCDEF12345678",
                "ABCDEF1234567890ABCDEF1234567890ABCDEF12",
                "168F56CD35982FA128EECC2B7247203866E163E3",
                "8E449D2F23A58C3CF831613DA247C66F5F348A7B"
            };
        }

        private bool IsDevelopmentEnvironment()
        {
            return ConstantsConfig.IsDevelopment;
        }
    }
}
