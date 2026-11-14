using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 透過 Windows App SDK <see cref="ResourceLoader"/> 或隨附 <c>.resw</c> 取得繁體中文文案。
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private readonly Lazy<ResourceLoader?> _resourceLoader;
    private readonly Lazy<Dictionary<string, string>> _fallbackStrings;
    private readonly ConcurrentDictionary<string, string> _resolvedStrings = new(StringComparer.Ordinal);

    public LocalizationService()
    {
        _resourceLoader = new Lazy<ResourceLoader?>(CreateResourceLoader);
        _fallbackStrings = new Lazy<Dictionary<string, string>>(LoadFallbackReswDictionary);
    }

    private static ResourceLoader? CreateResourceLoader()
    {
        try
        {
            return new ResourceLoader();
        }
        catch
        {
            return null;
        }
    }

    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_resolvedStrings.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        string resolved = ResolveString(key);
        _resolvedStrings.TryAdd(key, resolved);
        return resolved;
    }

    private string ResolveString(string key)
    {
        ResourceLoader? resourceLoader = _resourceLoader.Value;
        if (resourceLoader is not null)
        {
            try
            {
                string candidate = resourceLoader.GetString(key);
                if (!string.IsNullOrEmpty(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Fall back to parsed .resw dictionary when PRI key uses dotted syntax or in test host.
            }
        }

        return _fallbackStrings.Value.TryGetValue(key, out string? fallback) ? fallback : key;
    }

    public string Format(string key, params object[] args)
    {
        string template = GetString(key);
        return string.Format(CultureInfo.CurrentCulture, template, args);
    }

    private static Dictionary<string, string> LoadFallbackReswDictionary()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? reswPath = LocateReswFile();
        if (reswPath is null || !File.Exists(reswPath))
        {
            return result;
        }

        try
        {
            XDocument doc = XDocument.Load(reswPath);
            foreach (XElement dataElement in doc.Descendants("data"))
            {
                string? name = dataElement.Attribute("name")?.Value;
                string? val = dataElement.Element("value")?.Value;
                if (!string.IsNullOrEmpty(name) && val is not null)
                {
                    result[name] = val;
                }
            }
        }
        catch
        {
            // Return whatever was parsed; never crash app startup.
        }

        return result;
    }

    private static string? LocateReswFile()
    {
        string directPath = Path.Combine(AppContext.BaseDirectory, "Strings", "zh-Hant", "Resources.resw");
        if (File.Exists(directPath))
        {
            return directPath;
        }

        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "CourseRec", "Strings", "zh-Hant", "Resources.resw");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
