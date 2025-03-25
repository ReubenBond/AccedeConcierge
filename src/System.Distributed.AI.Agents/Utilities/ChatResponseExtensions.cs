using Microsoft.Extensions.AI;
using System.Text;
using System.Text.RegularExpressions;

namespace Accede.Service.Inference;

internal static partial class ChatResponseExtensions
{
    public static string ExtractText(this ChatResponse response)
    {
        if (response.Messages is { Count: 0 })
        {
            return string.Empty;
        }
        else if (response.Messages is { Count: 1 })
        {
            return ThinkTagRegex().Replace(response.Messages[0].Text, string.Empty).Trim();
        }

        var builder = new StringBuilder();
        foreach (var message in response.Messages)
        {
            var text = message.Text;
            if (text.Length > 0)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(text);
            }
        }

        return ThinkTagRegex().Replace(builder.ToString(), string.Empty).Trim();
    }

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkTagRegex();
}
