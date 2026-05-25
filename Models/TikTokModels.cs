using System.Text.Json.Serialization;

namespace TikTokEcoBelarus.Models;

// ── Корневой ответ ──────────────────────────────────────────
public class TikTokSearchResponse
{
    [JsonPropertyName("cursor")]
    public long Cursor { get; set; }

    [JsonPropertyName("has_more")]
    public int HasMore { get; set; }

    [JsonPropertyName("item_list")]
    public List<TikTokItem> ItemList { get; set; } = [];

    [JsonPropertyName("extra")]
    public TikTokExtra? Extra { get; set; }

    public bool HasMoreItems => HasMore == 1;
}

public class TikTokExtra
{
    [JsonPropertyName("logid")]
    public string LogId { get; set; } = "";

    [JsonPropertyName("now")]
    public long Now { get; set; }

    [JsonPropertyName("search_request_id")]
    public string SearchRequestId { get; set; } = "";
}

// ── Видео-объект ────────────────────────────────────────────
public class TikTokItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("desc")]
    public string Desc { get; set; } = "";

    [JsonPropertyName("createTime")]
    public long CreateTime { get; set; }

    [JsonPropertyName("isAd")]
    public bool IsAd { get; set; }

    [JsonPropertyName("author")]
    public TikTokAuthor Author { get; set; } = new();

    [JsonPropertyName("authorStats")]
    public TikTokAuthorStats AuthorStats { get; set; } = new();

    [JsonPropertyName("stats")]
    public TikTokStats Stats { get; set; } = new();

    [JsonPropertyName("challenges")]
    public List<TikTokChallenge> Challenges { get; set; } = [];

    [JsonPropertyName("textExtra")]
    public List<TikTokTextExtra> TextExtra { get; set; } = [];

    [JsonPropertyName("music")]
    public TikTokMusic? Music { get; set; }

    [JsonPropertyName("video")]
    public TikTokVideo Video { get; set; } = new();

    // Computed
    public DateTimeOffset CreatedAt =>
        DateTimeOffset.FromUnixTimeSeconds(CreateTime);

    public List<string> AllHashtags =>
        Challenges.Select(c => c.Title.ToLower()).ToList();
}

// ── Автор ───────────────────────────────────────────────────
public class TikTokAuthor
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("uniqueId")]
    public string UniqueId { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = ""; // bio — ключевое поле!

    [JsonPropertyName("secUid")]
    public string SecUid { get; set; } = "";

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    [JsonPropertyName("privateAccount")]
    public bool PrivateAccount { get; set; }
}

public class TikTokAuthorStats
{
    [JsonPropertyName("followerCount")]
    public long FollowerCount { get; set; }

    [JsonPropertyName("followingCount")]
    public long FollowingCount { get; set; }

    [JsonPropertyName("heartCount")]
    public long HeartCount { get; set; }

    [JsonPropertyName("videoCount")]
    public int VideoCount { get; set; }
}

// ── Статистика видео ────────────────────────────────────────
public class TikTokStats
{
    [JsonPropertyName("playCount")]
    public long PlayCount { get; set; }

    [JsonPropertyName("diggCount")]
    public long DiggCount { get; set; }

    [JsonPropertyName("commentCount")]
    public long CommentCount { get; set; }

    [JsonPropertyName("shareCount")]
    public long ShareCount { get; set; }

    [JsonPropertyName("collectCount")]
    public long CollectCount { get; set; }

    // Engagement Rate = (likes + comments + shares) / plays
    public double EngagementRate => PlayCount > 0
        ? (double)(DiggCount + CommentCount + ShareCount) / PlayCount
        : 0;
}

// ── Хэштеги ─────────────────────────────────────────────────
public class TikTokChallenge
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("desc")]
    public string Desc { get; set; } = "";
}

public class TikTokTextExtra
{
    [JsonPropertyName("hashtagName")]
    public string HashtagName { get; set; } = "";

    [JsonPropertyName("hashtagId")]
    public string HashtagId { get; set; } = "";

    [JsonPropertyName("type")]
    public int Type { get; set; } // 1 = hashtag
}

// ── Музыка и видео ──────────────────────────────────────────
public class TikTokMusic
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("authorName")]
    public string AuthorName { get; set; } = "";

    [JsonPropertyName("original")]
    public bool Original { get; set; }
}

public class TikTokVideo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("playAddr")]
    public string PlayAddr { get; set; } = "";

    [JsonPropertyName("downloadAddr")]
    public string DownloadAddr { get; set; } = "";

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("cover")]
    public string Cover { get; set; } = "";

    [JsonPropertyName("ratio")]
    public string Ratio { get; set; } = "";
}