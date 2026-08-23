using System.Text;
using System.Text.RegularExpressions;
using MeshKit.Core.Catalog;
using MeshKit.Web.Catalog;
using Microsoft.Data.Sqlite;

namespace MeshKit.Web.Search;

/// <summary>
/// Full-text + faceted search over every sellable model, on an in-memory SQLite database with FTS5
/// (BM25 ranking, Porter stemming, prefix matching). Rebuilt lazily whenever the catalog version
/// changes; a few hundred models index in milliseconds, so there is no incremental path.
/// </summary>
public sealed partial class SearchService : ISearchService, IDisposable
{
    private static readonly string[] FormatFilesOfInterest = ["glb", "fbx", "obj", "usdz", "stl", "3mf"];

    private readonly ICatalogService _catalog;
    private readonly ILogger<SearchService> _logger;
    private readonly Lock _gate = new();
    private SqliteConnection? _db;
    private int _builtVersion = -1;

    public SearchService(ICatalogService catalog, ILogger<SearchService> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public SearchResult Search(SearchQuery query)
    {
        var db = Ready();
        lock (_gate)
        {
            var (where, parameters, ftsJoin, orderBy) = Compose(query);
            var total = Scalar<long>(db, $"SELECT COUNT(*) FROM models m {ftsJoin} WHERE {where}", parameters);

            var hits = new List<SearchHit>();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = $"""
                    SELECT m.pack_slug, m.pack_name, m.model_slug, m.name, m.thumbnail, m.tags, m.category, m.style,
                           m.triangles, m.formats, m.price, m.currency, m.textured, {(ftsJoin.Length > 0 ? "bm25(models_fts, 10.0, 6.0, 4.0, 3.0, 1.0, 0.5)" : "0")} AS score
                    FROM models m {ftsJoin}
                    WHERE {where}
                    ORDER BY {orderBy}
                    LIMIT $limit OFFSET $offset
                    """;
                Bind(cmd, parameters);
                cmd.Parameters.AddWithValue("$limit", query.PageSize);
                cmd.Parameters.AddWithValue("$offset", (Math.Max(1, query.Page) - 1) * query.PageSize);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    hits.Add(new SearchHit(
                        PackSlug: reader.GetString(0),
                        PackName: reader.GetString(1),
                        ModelSlug: reader.GetString(2),
                        Name: reader.GetString(3),
                        Thumbnail: reader.IsDBNull(4) ? null : reader.GetString(4),
                        Tags: Split(reader.GetString(5)),
                        Category: reader.GetString(6),
                        Style: reader.GetString(7),
                        Triangles: reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        Formats: Split(reader.GetString(9)),
                        PriceAmount: reader.GetInt64(10),
                        PriceCurrency: reader.GetString(11),
                        PreviewTextured: reader.GetInt64(12) == 1,
                        Score: reader.GetDouble(13)));
                }
            }

            var facets = new SearchFacets(
                Categories: Facet(db, "m.category", where, parameters, ftsJoin),
                Styles: Facet(db, "m.style", where, parameters, ftsJoin),
                Tags: TagFacet(db, where, parameters, ftsJoin),
                Formats: FormatFacet(db, where, parameters, ftsJoin),
                Packs: Facet(db, "m.pack_name", where, parameters, ftsJoin));

            return new SearchResult(hits, (int)total, facets, query);
        }
    }

    public IReadOnlyList<string> Suggest(string prefix, int limit = 8)
    {
        var db = Ready();
        var needle = Tokenize(prefix).LastOrDefault();
        if (needle is null)
        {
            return [];
        }

        lock (_gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                SELECT value FROM (
                    SELECT name AS value, 0 AS kind FROM models WHERE (' ' || lower(name) || ' ') LIKE $p
                    UNION
                    SELECT tag AS value, 1 AS kind FROM model_tags WHERE (' ' || replace(tag, '-', ' ') || ' ') LIKE $p
                ) ORDER BY kind, value LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$p", "% " + needle + "%");
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = cmd.ExecuteReader();
            var result = new List<string>();
            while (reader.Read())
            {
                result.Add(reader.GetString(0));
            }

            return result;
        }
    }

    // ---- query composition ------------------------------------------------------------------

    private static (string Where, List<(string, object)> Parameters, string FtsJoin, string OrderBy) Compose(SearchQuery q)
    {
        var clauses = new List<string> { "1=1" };
        var parameters = new List<(string, object)>();
        var ftsJoin = string.Empty;

        var match = BuildMatch(q.Text);
        if (match is not null)
        {
            ftsJoin = "JOIN models_fts ON models_fts.rowid = m.id";
            clauses.Add("models_fts MATCH $match");
            parameters.Add(("$match", match));
        }

        if (q.Category is not null)
        {
            clauses.Add("m.category = $category");
            parameters.Add(("$category", q.Category));
        }

        if (q.Style is not null)
        {
            clauses.Add("m.style = $style");
            parameters.Add(("$style", q.Style));
        }

        if (q.Pack is not null)
        {
            clauses.Add("m.pack_slug = $pack");
            parameters.Add(("$pack", q.Pack));
        }

        if (q.Format is not null)
        {
            clauses.Add("(' ' || m.formats || ' ') LIKE $format");
            parameters.Add(("$format", $"% {q.Format.ToLowerInvariant()} %"));
        }

        if (q.MaxTriangles is { } max)
        {
            clauses.Add("m.triangles IS NOT NULL AND m.triangles <= $maxtris");
            parameters.Add(("$maxtris", max));
        }

        var i = 0;
        foreach (var tag in (q.Tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct())
        {
            clauses.Add($"EXISTS (SELECT 1 FROM model_tags t WHERE t.model_id = m.id AND t.tag = $tag{i})");
            parameters.Add(($"$tag{i}", tag));
            i++;
        }

        var orderBy = q.Sort switch
        {
            SearchSort.Name => "m.name COLLATE NOCASE, m.pack_name",
            SearchSort.TrianglesAsc => "m.triangles IS NULL, m.triangles ASC, m.name",
            SearchSort.TrianglesDesc => "m.triangles IS NULL, m.triangles DESC, m.name",
            SearchSort.PriceAsc => "m.price ASC, m.name",
            _ => match is not null ? "score ASC, m.name" : "m.pack_name, m.name",
        };

        return (string.Join(" AND ", clauses), parameters, ftsJoin, orderBy);
    }

    /// <summary>User text → FTS5 MATCH: each token quoted (no operator injection) and prefix-matched, AND-ed.</summary>
    internal static string? BuildMatch(string? text)
    {
        var tokens = Tokenize(text);
        return tokens.Count == 0 ? null : string.Join(" ", tokens.Select(t => $"\"{t}\"*"));
    }

    private static List<string> Tokenize(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : TokenPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value).Where(t => t.Length > 0).Take(12).ToList();

    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex TokenPattern();

    // ---- facets -----------------------------------------------------------------------------

    private static List<FacetValue> Facet(SqliteConnection db, string column, string where, List<(string, object)> parameters, string ftsJoin)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT {column}, COUNT(*) FROM models m {ftsJoin} WHERE {where} GROUP BY {column} ORDER BY COUNT(*) DESC, {column}";
        Bind(cmd, parameters);
        return ReadFacet(cmd);
    }

    private static List<FacetValue> TagFacet(SqliteConnection db, string where, List<(string, object)> parameters, string ftsJoin)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT t.tag, COUNT(*) FROM models m {ftsJoin} JOIN model_tags t ON t.model_id = m.id WHERE {where} GROUP BY t.tag ORDER BY COUNT(*) DESC, t.tag LIMIT 40";
        Bind(cmd, parameters);
        return ReadFacet(cmd);
    }

    private static List<FacetValue> FormatFacet(SqliteConnection db, string where, List<(string, object)> parameters, string ftsJoin)
    {
        var result = new List<FacetValue>();
        foreach (var format in FormatFilesOfInterest)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM models m {ftsJoin} WHERE {where} AND (' ' || m.formats || ' ') LIKE $fmt";
            Bind(cmd, parameters);
            cmd.Parameters.AddWithValue("$fmt", $"% {format} %");
            var count = (long)cmd.ExecuteScalar()!;
            if (count > 0)
            {
                result.Add(new FacetValue(format, (int)count));
            }
        }

        return result;
    }

    private static List<FacetValue> ReadFacet(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var list = new List<FacetValue>();
        while (reader.Read())
        {
            list.Add(new FacetValue(reader.GetString(0), reader.GetInt32(1)));
        }

        return list;
    }

    // ---- index build ------------------------------------------------------------------------

    private SqliteConnection Ready()
    {
        lock (_gate)
        {
            if (_db is not null && _builtVersion == _catalog.Version)
            {
                return _db;
            }

            var db = new SqliteConnection("Data Source=:memory:");
            db.Open();
            Build(db);
            _db?.Dispose();
            _db = db;
            _builtVersion = _catalog.Version;
            return db;
        }
    }

    private void Build(SqliteConnection db)
    {
        Exec(db, """
            CREATE TABLE models (
                id INTEGER PRIMARY KEY, pack_slug TEXT NOT NULL, pack_name TEXT NOT NULL, model_slug TEXT NOT NULL,
                name TEXT NOT NULL, thumbnail TEXT, tags TEXT NOT NULL, category TEXT NOT NULL, style TEXT NOT NULL,
                triangles INTEGER, formats TEXT NOT NULL, price INTEGER NOT NULL, currency TEXT NOT NULL, textured INTEGER NOT NULL);
            CREATE TABLE model_tags (model_id INTEGER NOT NULL, tag TEXT NOT NULL);
            CREATE INDEX ix_model_tags ON model_tags(tag, model_id);
            CREATE VIRTUAL TABLE models_fts USING fts5(name, tags, pack_name, category, description, prompt, tokenize = 'porter unicode61');
            """);

        using var tx = db.BeginTransaction();
        var id = 0;
        foreach (var pack in _catalog.Sellable)
        {
            foreach (var model in pack.Models)
            {
                id++;
                var tags = pack.TagList.Concat(model.TagList).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                var formats = model.Files.Select(f => f.Format).Where(FormatFilesOfInterest.Contains).Distinct().Order(StringComparer.Ordinal).ToList();
                var category = model.Category ?? pack.Category ?? "props";
                var style = pack.Style ?? "stylized";

                using (var cmd = db.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO models (id, pack_slug, pack_name, model_slug, name, thumbnail, tags, category, style, triangles, formats, price, currency, textured)
                        VALUES ($id, $ps, $pn, $ms, $name, $thumb, $tags, $cat, $style, $tris, $formats, $price, $cur, $textured)
                        """;
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.Parameters.AddWithValue("$ps", pack.Slug);
                    cmd.Parameters.AddWithValue("$pn", pack.Name);
                    cmd.Parameters.AddWithValue("$ms", model.Slug);
                    cmd.Parameters.AddWithValue("$name", model.Name);
                    cmd.Parameters.AddWithValue("$thumb", (object?)model.Thumbnail ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$tags", string.Join(' ', tags));
                    cmd.Parameters.AddWithValue("$cat", category);
                    cmd.Parameters.AddWithValue("$style", style);
                    cmd.Parameters.AddWithValue("$tris", (object?)model.Metadata?.Triangles ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$formats", string.Join(' ', formats));
                    cmd.Parameters.AddWithValue("$price", pack.Price.Amount);
                    cmd.Parameters.AddWithValue("$cur", pack.Price.Currency);
                    cmd.Parameters.AddWithValue("$textured", model.PreviewTextured ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = db.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO models_fts (rowid, name, tags, pack_name, category, description, prompt) VALUES ($id, $name, $tags, $pn, $cat, $desc, $prompt)";
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.Parameters.AddWithValue("$name", model.Name);
                    cmd.Parameters.AddWithValue("$tags", string.Join(' ', tags.Select(t => t.Replace('-', ' '))));
                    cmd.Parameters.AddWithValue("$pn", pack.Name);
                    cmd.Parameters.AddWithValue("$cat", $"{category} {style}");
                    cmd.Parameters.AddWithValue("$desc", pack.Description);
                    cmd.Parameters.AddWithValue("$prompt", model.Prompt);
                    cmd.ExecuteNonQuery();
                }

                foreach (var tag in tags)
                {
                    using var cmd = db.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO model_tags (model_id, tag) VALUES ($id, $tag)";
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.Parameters.AddWithValue("$tag", tag);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        tx.Commit();
        _logger.LogInformation("Search index built: {Models} model(s) from {Packs} pack(s)", id, _catalog.Sellable.Count);
    }

    private static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static T Scalar<T>(SqliteConnection db, string sql, List<(string, object)> parameters)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, parameters);
        return (T)cmd.ExecuteScalar()!;
    }

    private static void Bind(SqliteCommand cmd, List<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
    }

    private static IReadOnlyList<string> Split(string spaceSeparated) =>
        spaceSeparated.Length == 0 ? [] : spaceSeparated.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public void Dispose() => _db?.Dispose();
}
