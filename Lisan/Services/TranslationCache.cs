using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Lisan.Services;

public sealed class TranslationCache : IDisposable
{
    private readonly string path;
    private readonly ConcurrentDictionary<string, string> map = new(StringComparer.Ordinal);
    private readonly object saveGate = new();
    private int dirty;

    public TranslationCache(string directory)
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "dialogue-cache.json");
        Load();
    }

    public bool TryGet(string english, out string translation) =>
        TryGet(english, Plugin.Configuration.TargetLanguage, out translation);

    public bool TryGet(string english, string? language, out string translation)
    {
        var key = LangKey(language, english);
        if (map.TryGetValue(key, out translation!))
            return true;

        // Pre-0.3 cache had no language prefix (Arabic only).
        if (TargetLanguages.NormalizeCode(language) == "ar"
            && map.TryGetValue(Normalize(english), out translation!))
            return true;

        translation = null!;
        return false;
    }

    public void Set(string english, string translation) =>
        Set(english, translation, Plugin.Configuration.TargetLanguage);

    public void Set(string english, string translation, string? language)
    {
        map[LangKey(language, english)] = translation;
        Interlocked.Exchange(ref dirty, 1);
        SaveNow();
    }

    public void Clear()
    {
        map.Clear();
        Interlocked.Exchange(ref dirty, 1);
        SaveNow();
    }

    public int Count => map.Count;

    public void Dispose() => SaveNow();

    private void Load()
    {
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<ConcurrentDictionary<string, string>>(json);
            if (data is null)
                return;

            foreach (var (k, v) in data)
                map[k] = v;
        }
        catch
        {
            // ignore corrupt cache
        }
    }

    private void SaveNow()
    {
        if (Interlocked.Exchange(ref dirty, 0) == 0)
            return;

        lock (saveGate)
        {
            var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(path, json);
        }
    }

    private static string Normalize(string text) => text.Trim();

    private static string LangKey(string? language, string english) =>
        TargetLanguages.NormalizeCode(language) + "|" + Normalize(english);
}
