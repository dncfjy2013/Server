using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Server.Core.Certification
{
    public class SSLManager
    {
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
    }
}
