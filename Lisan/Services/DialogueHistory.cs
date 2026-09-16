using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Lisan.Services;

public sealed class HistoryEntry
{
    public DateTime Utc { get; set; } = DateTime.UtcNow;
    public string Speaker { get; set; } = "";
    public string English { get; set; } = "";
    public string Translation { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed class DialogueHistory : IDisposable
{
    private const int MaxEntries = 250;
    private readonly string path;
    private readonly object gate = new();
    private readonly List<HistoryEntry> entries = [];
    private bool dirty;

    public DialogueHistory(string directory)
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "dialogue-history.json");
        Load();
    }

    public HistoryEntry? Last
    {
        get
        {
            lock (gate)
                return entries.Count == 0 ? null : entries[^1];
        }
    }

    public int Count
    {
        get
        {
            lock (gate)
                return entries.Count;
        }
    }

    public List<HistoryEntry> Snapshot()
    {
        lock (gate)
            return entries.ToList();
    }

    public void Add(string source, string speaker, string english, string translation)
    {
        if (!Plugin.Configuration.SaveHistory)
            return;
        if (string.IsNullOrWhiteSpace(translation))
            return;
        if (TargetLanguages.IsBusyPlaceholder(translation))
            return;

        if (Plugin.Configuration.SaveHistory)
        {
            lock (gate)
            {
                if (entries.Count > 0)
                {
                    var last = entries[^1];
                    if (last.English == english && last.Translation == translation && last.Source == source)
                    {
                        if (Plugin.Configuration.AutoCopyTranslation)
                            Plugin.CopyText(translation);
                        return;
                    }
                }

                entries.Add(new HistoryEntry
                {
                    Utc = DateTime.UtcNow,
                    Source = source,
                    Speaker = speaker ?? "",
                    English = english ?? "",
                    Translation = translation,
                });

                while (entries.Count > MaxEntries)
                    entries.RemoveAt(0);

                dirty = true;
            }

            Save();
        }

        if (Plugin.Configuration.AutoCopyTranslation)
            Plugin.CopyText(translation);
    }

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
            dirty = true;
        }

        Save();
    }

    public string ExportText()
    {
        List<HistoryEntry> copy;
        lock (gate)
            copy = entries.ToList();

        var sb = new System.Text.StringBuilder();
        foreach (var e in copy)
        {
            sb.Append('[').Append(e.Utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")).Append("] ");
            sb.Append(e.Source);
            if (!string.IsNullOrWhiteSpace(e.Speaker))
                sb.Append(" / ").Append(e.Speaker);
            sb.AppendLine();
            sb.Append("EN: ").AppendLine(e.English);
            sb.Append(TargetLanguages.Current().Code.ToUpperInvariant()).Append(": ").AppendLine(e.Translation);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public string? WriteExportFile()
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir))
            return null;
        var file = Path.Combine(dir, "lisan-history.txt");
        File.WriteAllText(file, ExportText());
        return file;
    }

    public void Save()
    {
        List<HistoryEntry> copy;
        lock (gate)
        {
            if (!dirty)
                return;
            copy = entries.ToList();
            dirty = false;
        }

        try
        {
            var json = JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not save dialogue history");
        }
    }

    public void Dispose() => Save();

    private void Load()
    {
        try
        {
            if (!File.Exists(path))
                return;
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
            if (loaded is null)
                return;
            lock (gate)
            {
                entries.Clear();
                entries.AddRange(loaded.TakeLast(MaxEntries));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not load dialogue history");
        }
    }
}
