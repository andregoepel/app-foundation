using System.ComponentModel.DataAnnotations;

namespace AndreGoepel.AppFoundation.MailService;

internal record MailConfiguration
{
    [Required]
    public required string SenderName { get; init; }

    [Required]
    public required string SenderEmail { get; init; }

    [Required]
    public required string Server { get; init; }
    public int Port { get; init; } = 587;
    public bool UseSsl { get; init; }

    [Required]
    public required string Username { get; init; }

    [Required]
    public required string Password { get; init; }
    public bool Html { get; init; } = true;
}
