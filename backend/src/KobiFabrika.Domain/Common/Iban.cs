using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace KobiFabrika.Domain.Common;

/// <summary>Türkiye IBAN'ı: "TR" + 24 rakam (26 karakter), ISO 13616 mod-97 kontrolü. Boşluk ve tire atılır, harfler büyütülür.</summary>
public static class Iban
{
    public const string InvalidMessage = "Geçerli bir TR IBAN girin (TR ile başlayan 26 karakter).";

    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? iban)
    {
        iban = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var compact = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        if (compact.Length != 26 || !compact.StartsWith("TR", StringComparison.Ordinal) || !compact[2..].All(char.IsAsciiDigit))
        {
            return false;
        }

        // Ülke kodu ve kontrol haneleri sona alınır, harfler sayıya çevrilir (T=29, R=27); mod 97 == 1 olmalı.
        var rearranged = compact[4..] + "2927" + compact[2..4];
        if (BigInteger.Parse(rearranged) % 97 != 1)
        {
            return false;
        }

        iban = compact;
        return true;
    }

    /// <summary>"TR12 3456 …" biçiminde 4'erli gruplar.</summary>
    public static string Format(string iban) =>
        string.Join(' ', Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - (i * 4)))));
}
