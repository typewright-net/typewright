namespace Billing.Fluent.Domain;

/// <summary>Luhn checksum for account numbers. Non-digit input is invalid.</summary>
internal static class Luhn
{
    public static bool IsValid(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var sum = 0;
        var doubleDigit = false;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var c = value[i];
            if (c is < '0' or > '9')
            {
                return false;
            }

            var digit = c - '0';
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }
}
