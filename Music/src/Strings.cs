using Music.Models;
using Music.Services;

namespace Music;

/// <summary>
/// UI strings for the two supported languages. English is the second column,
/// simplified Chinese the first, so every string stays on one readable line.
/// </summary>
public static class Strings
{
    private static AppLanguage _language = AppLanguage.ChineseSimplified;

    public static AppLanguage Language => _language;

    public static bool IsEnglish => _language == AppLanguage.English;

    public static void SetLanguage(AppLanguage language)
    {
        _language = language;
    }

    public static string T(string chinese, string english) => IsEnglish ? english : chinese;

    // Navigation
    public static string AppSubtitle => T("本地音乐", "Local music");
    public static string SearchAllMusic => T("搜索全部音乐", "Search all music");
    public static string SearchAllPlaylists => T("搜索所有播放列表", "Search all playlists");
    public static string RecentProjects => T("最近的项目", "Recently played");
    public static string Favorites => T("收藏的项目", "Favorites");
    public static string FavoriteTracks => T("收藏的曲目", "Favorite tracks");
    public static string FavoriteEmptyTitle => T("还没有收藏的歌曲", "No favorites yet");
    public static string FavoriteEmptyDetail => T("在曲目行、播放页或播放条上点击收藏图标，收藏的歌会出现在这里。", "Press the star on a track row, the player page or the player bar to keep songs here.");
    public static string SearchFavorites => T("搜索收藏的项目", "Search favorites");
    public static string SortFavorites => T("排序收藏的项目", "Sort favorites");
    public static string StatusFavorited(string title) => T($"已收藏 · {title}", $"Added to favorites · {title}");
    public static string StatusUnfavorited(string title) => T($"已取消收藏 · {title}", $"Removed from favorites · {title}");
    public static string StatusFavoriteNoTrack => T("当前没有正在播放的曲目", "Nothing is playing right now");
    public static string Playlists => T("播放列表", "Playlists");
    public static string DefaultList => T("默认列表", "Default list");
    public static string AddPlaylist => T("添加播放列表", "Add playlist");
    public static string Settings => T("设置", "Settings");

    // Player
    public static string PreviousTrack => T("上一首", "Previous");
    public static string NextTrack => T("下一首", "Next");
    public static string PlayPause => T("播放 / 暂停", "Play / Pause");
    public static string Play => T("播放", "Play");
    public static string Pause => T("暂停", "Pause");
    public static string PlayOrPause => T("播放或暂停", "Play or pause");
    public static string Mute => T("静音", "Mute");
    public static string Unmute => T("取消静音", "Unmute");
    public static string Volume => T("音量", "Volume");
    public static string CurrentVolume => T("当前音量", "Current volume");
    public static string Progress => T("播放进度", "Playback progress");
    public static string OpenPlayerPage => T("打开播放页面", "Open player page");
    public static string NotPlaying => T("未播放", "Not playing");
    public static string PickSongHint => T("从播放列表选择一首歌曲", "Pick a song from a playlist");
    public static string Ready => T("准备就绪", "Ready");
    public static string Loading => T("正在载入", "Loading");
    public static string Loaded => T("已载入", "Loaded");
    public static string Playing => T("正在播放", "Playing");
    public static string Paused => T("已暂停", "Paused");
    public static string PlaybackEnded => T("播放结束", "Playback ended");
    public static string PlaybackFailed => T("播放失败", "Playback failed");
    public static string Favorite => T("收藏", "Favorite");
    public static string Unfavorite => T("取消收藏", "Remove from favorites");
    public static string MoreOptions => T("更多选项", "More options");
    public static string AddToPlaylist => T("添加到播放列表", "Add to playlist");

    public static string RepeatTooltip(RepeatMode mode) => mode switch
    {
        RepeatMode.All => T("循环播放：列表循环 · 点击切换为单曲循环", "Repeat: all · click for repeat one"),
        RepeatMode.One => T("循环播放：单曲循环 · 点击关闭循环播放", "Repeat: one · click to turn off"),
        _ => T("循环播放：关闭 · 点击开启列表循环", "Repeat: off · click for repeat all")
    };

    public static string RepeatState(RepeatMode mode) => mode switch
    {
        RepeatMode.All => T("循环播放：列表循环", "Repeat: all"),
        RepeatMode.One => T("循环播放：单曲循环", "Repeat: one"),
        _ => T("循环播放：关闭", "Repeat: off")
    };

    public static string RepeatName(RepeatMode mode) => mode switch
    {
        RepeatMode.All => T("列表循环", "Repeat all"),
        RepeatMode.One => T("单曲循环", "Repeat one"),
        _ => T("循环播放", "Repeat")
    };

    // Toolbar
    public static string Back => T("返回", "Back");
    public static string Import => T("导入音乐", "Import music");
    public static string ImportShortcut => T("导入音乐（Ctrl+O）", "Import music (Ctrl+O)");
    public static string Search => T("搜索", "Search");
    public static string SearchQueue => T("搜索播放列表", "Search playlist");
    public static string SearchRecent => T("搜索最近项目", "Search recent");
    public static string SearchCustom => T("搜索当前播放列表", "Search this playlist");
    public static string Sort => T("排序", "Sort");
    public static string SortRecent => T("排序最近项目", "Sort recent items");
    public static string SortCustom => T("排序当前播放列表", "Sort this playlist");
    public static string ClearRecent => T("清空最近的项目", "Clear recently played");
    public static string AddSongs => T("添加歌曲", "Add songs");
    public static string ClearSearch => T("清除搜索", "Clear search");

    // Lists and empty states
    public static string Queue => T("播放队列", "Queue");
    public static string RecentTracks => T("最近播放的曲目", "Recently played tracks");
    public static string CustomTracks => T("当前播放列表曲目", "Tracks in this playlist");
    public static string SearchResults => T("搜索结果", "Search results");
    public static string QueueEmptyTitle => T("队列还是空的", "Your queue is empty");
    public static string QueueEmptyDetail => T("导入或直接拖入几首本地音乐，创建你的播放列表。", "Import or drop a few local songs to build your playlist.");
    public static string NoMatches => T("没有匹配的曲目", "No matching tracks");
    public static string NoMatchesReset => T("换一个关键词，或清空搜索框恢复完整播放列表。", "Try another keyword, or clear the search box to show everything.");
    public static string NoMatchesClear => T("换一个关键词，或清空搜索框。", "Try another keyword, or clear the search box.");
    public static string RecentEmptyTitle => T("还没有最近播放的项目", "Nothing played yet");
    public static string RecentEmptyDetail => T("播放任意曲目后，这里会记录最近听过的内容。", "Songs you play will show up here.");
    public static string RecentNoMatchTitle => T("没有匹配的最近项目", "No matching recent items");
    public static string CustomEmptyTitle => T("此播放列表还没有歌曲", "This playlist is empty");
    public static string CustomEmptyDetail => T("用曲目行的“添加到播放列表”按钮，或点击右上角加号导入歌曲。", "Use “Add to playlist” on a row, or the plus button to import songs.");
    public static string CustomStatusEmpty => T("可以拖入音频文件添加到当前播放列表", "Drop audio files here to add them");
    public static string CustomStatusCount(int count) => T($"当前播放列表共 {count} 首曲目", $"{count} songs in this playlist");
    public static string DropToPlay => T("松开即可添加到播放队列", "Drop to add to the queue");
    public static string DropToCustom => T("松开即可添加到当前播放列表", "Drop to add to this playlist");
    public static string DropHint => T("支持同时拖入多个音频文件", "You can drop several audio files at once");
    public static string RemoveSelected => T("移除选中曲目", "Remove selected track");
    public static string ClearQueue => T("清空队列", "Clear queue");
    public static string Initializing => T("正在准备播放列表…", "Preparing your playlist…");

    // Search page
    public static string SearchScopeAll => T("搜索范围：所有播放列表", "Searching all playlists");
    public static string SearchIndexing(int count) => T($"{count} 个结果 · 正在索引所有播放列表…", $"{count} results · indexing playlists…");
    public static string SearchResultCount(int count) => T($"{count} 个结果 · 搜索范围：所有播放列表", $"{count} results · all playlists");
    public static string Searching => T("正在搜索…", "Searching…");
    public static string SearchingDetail => T("正在读取所有播放列表中的曲目信息。", "Reading track details from all playlists.");
    public static string SearchNoResultsDetail => T("换一个关键词，或检查播放列表里是否包含这首歌。", "Try another keyword, or check whether the song is in a playlist.");
    public static string AddToDefaultList => T("添加到默认列表", "Add to default list");
    public static string RemoveFromDefaultList => T("从默认列表移除", "Remove from default list");
    public static string AlreadyInDefaultList => T("这首歌已经在默认列表里", "This song is already in the default list");
    public static string AddedToDefaultList(string title) => T($"已添加到默认列表 · {title}", $"Added to default list · {title}");

    // Settings page
    public static string SettingsSubtitle => T("应用语言、界面主题与版本信息", "Language, theme and version");
    public static string ApplicationLanguage => T("应用语言", "App language");
    public static string InterfaceTheme => T("界面主题", "Theme");
    public static string About => T("关于", "About");
    public static string Chinese => T("简体中文", "简体中文");
    public static string English => T("英语", "English");
    public static string SystemTheme => T("跟随系统", "Use system setting");
    public static string LightTheme => T("浅色", "Light");
    public static string DarkTheme => T("深色", "Dark");
    public static string VersionLine(string informational) => T($"版本 {informational} · WinUI 3 · .NET 10 · Windows x64", $"Version {informational} · WinUI 3 · .NET 10 · Windows x64");
    public static string SdkLine => T("Windows App SDK 1.8 · MIT License", "Windows App SDK 1.8 · MIT License");
    public static string Copyright => T("© 2026 Wetgoto · 本地音乐播放器", "© 2026 Wetgoto · Local music player");
    public static string GitHubRepository => T("GitHub 仓库", "GitHub repository");

    // Menus
    public static string SortByRecent => T("按最近播放排序", "Recently played first");
    public static string SortByName => T("按名称排序", "By name");
    public static string SortByDuration => T("按时长排序", "By duration");
    public static string SortByAdded => T("按添加日期排序", "By date added");
    public static string SortByAddedOrder => T("按添加顺序排序", "In order added");
    public static string SortedByName => T("已按名称排序", "Sorted by name");
    public static string SortedByDuration => T("已按时长排序", "Sorted by duration");
    public static string SortedByAdded => T("已按添加日期排序", "Sorted by date added");
    public static string Remove => T("移除", "Remove");
    public static string ViewDetails => T("查看详细信息", "View details");
    public static string Details => T("详细信息", "Details");
    public static string Rename => T("重命名", "Rename");
    public static string Delete => T("删除", "Delete");
    public static string NoCustomPlaylists => T("还没有自定义播放列表", "No custom playlists yet");
    public static string PlaylistNameHint => T("播放列表名称", "Playlist name");
    public static string NewPlaylist => T("新建播放列表", "New playlist");
    public static string Playlist => T("播放列表", "Playlist");
    public static string AddToQueueCaption => T("添加到播放队列", "Add to queue");
    public static string AddToPlaylistCaption => T("添加到当前播放列表", "Add to this playlist");

    public static string ClearRecentTitle => T("清空最近的项目？", "Clear recently played?");
    public static string ClearRecentDetail => T("只会清空最近播放记录，不会删除音乐文件。", "Only the play history is cleared. Music files stay untouched.");
    public static string Clear => T("清空", "Clear");
    public static string Cancel => T("取消", "Cancel");
    public static string Close => T("关闭", "Close");

    public static string DeletePlaylistTitle(string name) => T($"删除“{name}”？", $"Delete “{name}”?");
    public static string DeletePlaylistDetail => T("只会删除这个播放列表条目，不会删除音乐文件。", "Only this playlist is deleted. Music files stay untouched.");

    public static string MissingFilesTitle => T("找不到音乐文件", "Music files not found");
    public static string MissingFilesRemove => T("是否要从播放列表中移除这些条目？", "Remove these entries from the playlist?");
    public static string RemoveFromList => T("从列表移除", "Remove from list");
    public static string Keep => T("保留", "Keep");

    // Details dialog
    public static string FileFormat => T("文件格式", "Format");
    public static string Bitrate => T("比特率", "Bitrate");
    public static string FileLocation => T("文件位置", "Location");
    public static string FileSize => T("文件大小", "Size");
    public static string Album => T("专辑", "Album");
    public static string AlbumArtist => T("专辑艺术家", "Album artist");
    public static string Title => T("标题", "Title");
    public static string Duration => T("时长", "Duration");
    public static string AlbumCover => T("专辑封面", "Cover art");
    public static string Unknown => T("未知", "Unknown");
    public static string NoCover => T("无封面", "No cover");
    public static string FileNotFound => T("文件不存在", "File not found");
    public static string FileMissing => T("文件缺失", "File missing");
    public static string UnknownArtist => T("未知艺术家", "Unknown artist");
    public static string UnknownAlbum => T("未知专辑", "Unknown album");
    public static string UnknownAlbumArtist => T("未知专辑艺术家", "Unknown album artist");
    public static string UnknownBitrate => T("未知比特率", "Unknown bitrate");
    public static string UnknownSize => T("未知大小", "Unknown size");
    public static string FileNotFoundDoubleClick => T("文件不存在，双击可移除", "File not found, double-click to remove");
    public static string UnsupportedFormat => T("无法访问文件，或文件格式不受支持", "The file could not be read, or its format is not supported");

    // Status messages
    public static string StatusInitFailed(string message) => T($"初始化播放列表失败：{message}", $"Could not load the playlist: {message}");
    public static string StatusReadyToImport => T("就绪 · 导入或拖入音乐", "Ready · import or drop music");
    public static string StatusRestoring => T("正在恢复上次的播放列表…", "Restoring your playlist…");
    public static string StatusRestored(int count) => T($"已恢复上次的播放列表 · {count} 首", $"Restored your playlist · {count} songs");
    public static string StatusRestoredWithMissing(int count, int missing) => T($"已恢复 {count} 首 · {missing} 个文件缺失", $"Restored {count} songs · {missing} files missing");
    public static string StatusRemovedMissing(int removed) => T($"已从播放列表移除 {removed} 个缺失条目", $"Removed {removed} missing entries");
    public static string StatusNoMissingToRemove => T("没有可移除的缺失条目", "No missing entries to remove");
    public static string StatusReadingFiles => T("正在读取音乐，请稍候再拖入", "Still reading music, try again in a moment");
    public static string StatusDropReadFailed(string message) => T($"无法读取拖入的文件：{message}", $"Could not read the dropped files: {message}");
    public static string StatusNoAudioDropped => T("拖入的内容中没有可导入的音频文件", "Nothing droppable was found in what you dropped");
    public static string StatusPickerFailed(string message) => T($"无法打开文件选择器：{message}", $"Could not open the file picker: {message}");
    public static string StatusAllAlreadyInQueue => T("所选曲目已在队列中", "Those songs are already in the queue");
    public static string StatusNoAudioSelected => T("没有可导入的音频文件", "No audio files to import");
    public static string StatusReadingTrack(int index, int total, string name) => T($"正在读取 {index}/{total} · {name}", $"Reading {index}/{total} · {name}");
    public static string StatusImportFailed => T("导入失败，文件可能已移动或格式不受支持", "Import failed. The file may have moved or use an unsupported format");
    public static string StatusNothingAdded => T("没有新增曲目", "No new tracks were added");
    public static string StatusMissingFile(string title) => T($"文件缺失 · {title}", $"File missing · {title}");
    public static string StatusNowPlaying(string title) => T($"正在播放 · {title}", $"Playing · {title}");
    public static string StatusPausedWith(string title) => T($"已暂停 · {title}", $"Paused · {title}");
    public static string StatusLoadFailed(string title, string message) => T($"无法载入《{title}》：{message}", $"Could not load “{title}”: {message}");
    public static string StatusLoading(string title) => T($"正在载入 · {title}", $"Loading · {title}");
    public static string StatusQueueEmpty => T("队列为空，先导入音乐", "The queue is empty, import some music first");
    public static string StatusNoPlayable => T("队列中没有可播放的曲目", "The queue has nothing playable");
    public static string StatusFirstInQueue => T("已经是队列第一首", "This is the first song in the queue");
    public static string StatusLastInQueue => T("已经是队列最后一首", "This is the last song in the queue");
    public static string StatusRemoved(string title) => T($"已移除《{title}》", $"Removed “{title}”");
    public static string StatusQueueCleared(int count) => T($"已清空队列 · {count} 首曲目", $"Queue cleared · {count} songs");
    public static string StatusPlaybackFailed(string message) => T($"播放失败：{message}", $"Playback failed: {message}");
    public static string StatusQueueFinished => T("队列已播放完毕", "The queue has finished playing");
    public static string StatusMuted => T("已静音", "Muted");
    public static string StatusUnmuted => T("已恢复音量", "Sound restored");
    public static string StatusFavoriteSaveFailed => T("收藏状态暂时无法保存", "Could not save the favorite state");
    public static string StatusAddedToPlaylist(string name) => T($"已添加到播放列表《{name}》", $"Added to playlist “{name}”");
    public static string StatusTheme(string name) => T($"界面主题：{name}", $"Theme: {name}");
    public static string StatusLanguage(string name) => T($"应用语言：{name}", $"Language: {name}");

    public static string StatusImported(int added, int skipped) => skipped > 0
        ? T($"已导入 {added} 首音乐 · 跳过 {skipped} 首", $"Imported {added} songs · skipped {skipped}")
        : T($"已导入 {added} 首音乐", $"Imported {added} songs");

    // Counts
    public static string MatchCount(int count) => T($"{count} 个匹配", $"{count} matches");
    public static string ItemCount(int count) => T($"{count} 个项目", $"{count} items");
    public static string TrackCount(int count, int missing) => missing > 0
        ? T($"{count} 首曲目 · {missing} 个缺失", $"{count} songs · {missing} missing")
        : T($"{count} 首曲目", $"{count} songs");

    public static string MissingFilesSingle => T("这个文件已经不在原来的位置：", "This file is no longer where it was:");
    public static string MissingFilesMany(int count) => T($"有 {count} 个文件已经不在原来的位置：", $"{count} files are no longer where they were:");
    public static string MissingFilesMore(int count) => T($"……还有 {count} 个文件", $"…and {count} more files");
}
