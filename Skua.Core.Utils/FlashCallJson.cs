using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace Skua.Core.Utils;

/// <summary>
/// Builds the macOS Flash/Ruffle bridge request payload without allocating an anonymous type
/// or calling Newtonsoft's SerializeObject for every game call.
/// </summary>
public static class FlashCallJson
{
    public static string Build(int id, string function, object[] args)
    {
        StringBuilder sb = new(64 + function.Length + (args.Length * 16));
        sb.Append("{\"id\":").Append(id).Append(",\"fn\":");
        AppendJsonString(sb, function);
        sb.Append(",\"args\":[");
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            AppendJsonValue(sb, args[i]);
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static void AppendJsonValue(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case string s:
                AppendJsonString(sb, s);
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case int i:
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                break;
            case long l:
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                break;
            case float f:
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                break;
            case double d:
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                break;
            case decimal m:
                sb.Append(m.ToString(CultureInfo.InvariantCulture));
                break;
            case Enum e:
                AppendJsonString(sb, e.ToString());
                break;
            default:
                sb.Append(JsonConvert.SerializeObject(value));
                break;
        }
    }

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        sb.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }
}
