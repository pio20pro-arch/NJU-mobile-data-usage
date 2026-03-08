using System.Text.Json;

namespace NjuTrayApp.Services;

public static class ProductParser
{
    public static (decimal DomesticMb, decimal RoamingMb) ParseUsageFromProducts(string productsJson)
    {
        using var document = JsonDocument.Parse(productsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return (0m, 0m);
        }

        decimal domesticTotal = 0m;
        decimal roamingTotal = 0m;
        foreach (var product in document.RootElement.EnumerateArray())
        {
            if (!product.TryGetProperty("balances", out var balancesElement))
            {
                continue;
            }

            domesticTotal += SumMatchingBalances(balancesElement, IsDomesticBalanceName);
            roamingTotal += SumMatchingBalances(balancesElement, IsRoamingBalanceName);
        }

        return (domesticTotal, roamingTotal);
    }

    public static decimal ParseMonthlyMbFromProducts(string productsJson)
    {
        return ParseUsageFromProducts(productsJson).DomesticMb;
    }

    private static decimal SumMatchingBalances(JsonElement balancesElement, Func<string, bool> namePredicate)
    {
        return balancesElement.ValueKind switch
        {
            JsonValueKind.Array => balancesElement.EnumerateArray().Sum(balance => ParseBalanceValue(balance, namePredicate)),
            JsonValueKind.Object => ParseBalanceValue(balancesElement, namePredicate),
            _ => 0m
        };
    }

    private static decimal ParseBalanceValue(JsonElement balance, Func<string, bool> namePredicate)
    {
        var type = GetStringOrEmpty(balance, "type");
        var name = GetStringOrEmpty(balance, "name");
        if (!string.Equals(type, "ASSET", StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        if (!namePredicate(name))
        {
            return 0m;
        }

        var rawValue = GetStringOrEmpty(balance, "currentValue");
        if (decimal.TryParse(rawValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (balance.TryGetProperty("currentValue", out var valueProp) && valueProp.ValueKind == JsonValueKind.Number && valueProp.TryGetDecimal(out var numeric))
        {
            return numeric;
        }

        return 0m;
    }

    private static bool IsDomesticBalanceName(string name)
    {
        return name.Contains("OPL Data Asset", StringComparison.OrdinalIgnoreCase)
            && name.Contains("monthly", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRoamingBalanceName(string name)
    {
        return name.Contains("Data Package FUP", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStringOrEmpty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => value.ToString()
        };
    }
}
