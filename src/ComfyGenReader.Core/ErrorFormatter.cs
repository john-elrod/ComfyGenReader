using System.Text;

namespace ComfyGenReader.Core;

public static class ErrorFormatter
{
    public static string Format(Exception ex)
    {
        var sb = new StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e != ex) sb.Append("\n\n--- Inner exception ---\n");
            sb.Append(e.GetType().FullName).Append(": ").Append(e.Message);
            if (e.StackTrace is { } stack) sb.Append("\n\n").Append(stack);
        }
        return sb.ToString();
    }
}
