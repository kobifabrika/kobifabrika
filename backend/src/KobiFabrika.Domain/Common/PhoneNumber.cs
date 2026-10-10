using System.Diagnostics.CodeAnalysis;

namespace KobiFabrika.Domain.Common;

/// <summary>
/// Türkiye telefon numaralarını E.164 biçimine (+90XXXXXXXXXX) çevirir.
/// Kabul edilen girdiler: "0532 123 45 67", "532-123-4567", "+90 532 123 45 67", "90 (212) 123 45 67".
/// Ulusal numara 10 hanedir ve 2, 3, 4 (sabit hat), 5 (cep) veya 8 (850 vb.) ile başlar.
/// </summary>
public static class PhoneNumber
{
    public const string InvalidMessage = "Geçerli bir telefon numarası girin (ör. 0532 123 45 67).";

    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? e164)
    {
        e164 = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        foreach (var ch in trimmed)
        {
            if (!char.IsAsciiDigit(ch) && ch is not (' ' or '-' or '(' or ')' or '+' or '.'))
            {
                return false;
            }
        }

        if (trimmed.IndexOf('+') > 0)
        {
            return false;
        }

        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        var national = digits switch
        {
            { Length: 12 } when digits.StartsWith("90", StringComparison.Ordinal) => digits[2..],
            { Length: 11 } when digits[0] == '0' && !trimmed.StartsWith('+') => digits[1..],
            { Length: 10 } when !trimmed.StartsWith('+') => digits,
            _ => null,
        };

        if (national is null || national[0] is not ('2' or '3' or '4' or '5' or '8'))
        {
            return false;
        }

        e164 = "+90" + national;
        return true;
    }
}
