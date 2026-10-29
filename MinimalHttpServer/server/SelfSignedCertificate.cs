using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// 开发期工具：生成自签名证书（QUIC 强制 TLS 1.3，没有证书跑不起来）
// Kestrel 里对应 CertificateManager 的开发证书逻辑

public static class SelfSignedCertificate
{
    public static X509Certificate2 Create(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // SAN —— 让证书对 localhost 有效
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(subjectName);
        sanBuilder.AddDnsName("localhost");
        request.CertificateExtensions.Add(sanBuilder.Build(true));

        // 叶子证书
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));

        // 增强密钥用法：服务器认证
        var ekus = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(ekus, true));

        var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        // 导出 pfx 再加载，PrivateKey 才能被 Schannel 使用
        var pfxBytes = generated.Export(X509ContentType.Pfx);
        return new X509Certificate2(pfxBytes);
    }
}
