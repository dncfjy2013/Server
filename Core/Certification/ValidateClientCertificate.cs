using Server.Core.Common;
using Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Server.Core.Certification
{
    public class ValidateCertificate
    {
        private ILogger _logger;
        public ValidateCertificate(ILogger logger) 
        {
            _logger = logger;
        }


        // 自定义客户端证书验证回调
        private bool ValidateClientCertificate(object sender, X509Certificate? certificate, X509Chain? chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            // 基础验证：证书不能为空
            if (certificate == null)
            {
                _logger.Critical("Client certificate is null - Authentication failed");
                return false;
            }

            _logger.Info($"Client certificate received: {certificate.Subject}");
            _logger.Debug($"Certificate details: Issuer={certificate.Issuer}, Serial={certificate.GetSerialNumberString()}");

            // 开发环境配置：允许自签名证书
            bool isDevelopment = IsDevelopmentEnvironment();
            _logger.Trace($"Environment mode: {(isDevelopment ? "Development" : "Production")}");

            // 证书指纹验证
            bool thumbprintValid = ValidateCertificateThumbprint(certificate);
            _logger.Debug($"Certificate thumbprint validation: {(thumbprintValid ? "Passed" : "Failed")}");

            if (!thumbprintValid)
            {
                _logger.Warn($"Certificate thumbprint mismatch: {certificate.GetCertHashString()}");
                if (!isDevelopment)
                {
                    _logger.Error("Rejected in non-development environment due to thumbprint mismatch");
                    return false;
                }
                _logger.Warn("Allowing certificate with invalid thumbprint in development environment");
            }

            // 处理证书链错误
            if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            {
                if (chain == null)
                {
                    _logger.Critical("Certificate chain is null but chain error detected - Authentication failed");
                    return false;
                }

                bool hasNonTrustedRootError = false;
                bool hasCriticalError = false;

                _logger.Warn($"Certificate chain validation issues detected: {sslPolicyErrors}");

                foreach (var status in chain.ChainStatus)
                {
                    _logger.Debug($"Chain status: {status.Status} - {status.StatusInformation}");

                    // 标记严重错误
                    if (status.Status == X509ChainStatusFlags.InvalidBasicConstraints ||
                        status.Status == X509ChainStatusFlags.InvalidNameConstraints ||
                        status.Status == X509ChainStatusFlags.InvalidPolicyConstraints)
                    {
                        _logger.Error($"Critical chain error: {status.Status}");
                        hasCriticalError = true;
                    }

                    // 标记非信任根证书错误
                    if (status.Status == X509ChainStatusFlags.UntrustedRoot)
                    {
                        hasNonTrustedRootError = true;
                    }
                }

                // 生产环境下，任何链错误都视为失败
                if (!isDevelopment)
                {
                    _logger.Error("Certificate chain validation failed in production environment");
                    return false;
                }

                // 开发环境下仅允许非信任根错误
                if (hasCriticalError || !hasNonTrustedRootError || chain.ChainStatus.Length > 1)
                {
                    _logger.Error("Non-acceptable chain errors detected even in development environment");
                    return false;
                }

                _logger.Warn("Accepting certificate with untrusted root (development environment)");
            }

            // 处理其他证书错误
            if ((sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            {
                _logger.Error($"Certificate validation errors: {sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors}");
                return false;
            }

            // 证书主体白名单验证
            string[] allowedSubjects = { "CN=trusted-client", "CN=api-client" };
            bool subjectAllowed = allowedSubjects.Contains(certificate.Subject);

            _logger.Debug($"Certificate subject validation: {(subjectAllowed ? "Allowed" : "Rejected")} - {certificate.Subject}");
            if (!subjectAllowed)
            {
                _logger.Warn($"Certificate subject not in allowed list: {certificate.Subject}");
                return false;
            }

            // 证书有效期验证
            DateTime now = DateTime.UtcNow;
            if (DateTime.TryParse(certificate.GetExpirationDateString(), out DateTime expiryDate) &&
                expiryDate < now)
            {
                _logger.Error($"Certificate has expired: {expiryDate:u} (Current: {now:u})");
                return false;
            }

            _logger.Info("Client certificate validation passed successfully");
            return true;
        }

        // 证书指纹验证方法
        private bool ValidateCertificateThumbprint(X509Certificate certificate)
        {
            using var x509Cert = new X509Certificate2(certificate);
            string certificateHash = BitConverter.ToString(x509Cert.GetCertHash()).Replace("-", "").ToUpperInvariant();

            // 从配置加载允许的指纹列表
            var allowedThumbprints = LoadAllowedThumbprints();

            _logger.Debug($"Verifying certificate thumbprint: {certificateHash}");

            bool isValid = allowedThumbprints.Contains(certificateHash);
            if (!isValid)
            {
                _logger.Debug($"Thumbprint not in allowed list. Allowed: {string.Join(", ", allowedThumbprints)}");
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
                "168F56CD35982FA128EECC2B7247203866E163E3"
            };
        }

        // 判断当前是否为开发环境
        private bool IsDevelopmentEnvironment()
        {
            return ConstantsConfig.IsDevelopment;
        }
    }
}
