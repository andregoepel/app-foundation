using AndreGoepel.AppFoundation.Hosting;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using NSubstitute;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public class EnsureKeyRingProtectedTests
{
    [Fact]
    public void NonDevelopment_WithoutEncryptor_NotAllowed_Throws()
    {
        // Arrange / Act / Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Initialization.EnsureKeyRingProtected(
                isDevelopment: false,
                allowUnprotectedKeyRing: false,
                xmlEncryptor: null
            )
        );
        Assert.Contains("key ring", exception.Message);
    }

    [Fact]
    public void NonDevelopment_WithEncryptor_DoesNotThrow()
    {
        // Arrange
        var encryptor = Substitute.For<IXmlEncryptor>();

        // Act / Assert — encryption configured (cert / Key Vault / KMS), so allowed.
        Initialization.EnsureKeyRingProtected(
            isDevelopment: false,
            allowUnprotectedKeyRing: false,
            xmlEncryptor: encryptor
        );
    }

    [Fact]
    public void NonDevelopment_WithoutEncryptor_ExplicitlyAllowed_DoesNotThrow()
    {
        // Act / Assert — host accepted an unencrypted key ring.
        Initialization.EnsureKeyRingProtected(
            isDevelopment: false,
            allowUnprotectedKeyRing: true,
            xmlEncryptor: null
        );
    }

    [Fact]
    public void Development_WithoutEncryptor_DoesNotThrow()
    {
        // Act / Assert — local development never requires key encryption.
        Initialization.EnsureKeyRingProtected(
            isDevelopment: true,
            allowUnprotectedKeyRing: false,
            xmlEncryptor: null
        );
    }
}
