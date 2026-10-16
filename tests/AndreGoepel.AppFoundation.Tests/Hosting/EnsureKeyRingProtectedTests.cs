using AndreGoepel.AppFoundation.Hosting;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using NSubstitute;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public sealed class EnsureKeyRingProtectedTests
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

        // Act
        var exception = Record.Exception(() =>
            Initialization.EnsureKeyRingProtected(
                isDevelopment: false,
                allowUnprotectedKeyRing: false,
                xmlEncryptor: encryptor
            )
        );

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void NonDevelopment_WithoutEncryptor_ExplicitlyAllowed_DoesNotThrow()
    {
        // Act
        var exception = Record.Exception(() =>
            Initialization.EnsureKeyRingProtected(
                isDevelopment: false,
                allowUnprotectedKeyRing: true,
                xmlEncryptor: null
            )
        );

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Development_WithoutEncryptor_DoesNotThrow()
    {
        // Act
        var exception = Record.Exception(() =>
            Initialization.EnsureKeyRingProtected(
                isDevelopment: true,
                allowUnprotectedKeyRing: false,
                xmlEncryptor: null
            )
        );

        // Assert
        Assert.Null(exception);
    }
}
