using ExCord.Models;
using System.Globalization;

namespace ExCord.Services;

public static class LocalizationService
{
    private static readonly LocalizedText EnglishText = new()
    {
        InputWindowTitle = "ExCord Input",
        InputHeader = "Type your message",
        SettingsWindowTitle = "ExCord Settings",
        TabGeneral = "General",
        TabTts = "TTS",
        TabGifTalk = "GifTalk",
        SectionApplication = "Application",
        SectionFeatures = "Features",
        SectionLanguage = "Language",
        StartWithWindows = "Start with Windows",
        TextToSpeech = "Text-to-Speech",
        GifTalk = "GifTalk",
        GlobalHotkey = "Global Hotkey",
        TtsVoice = "TTS Voice",
        VirtualOutputDevice = "Virtual Output Device",
        OutputVolume = "Output Volume",
        MonitorSound = "Play monitor sound on default speaker",
        GifUrl = "GIF URL",
        Apply = "Apply",
        EditMode = "Edit Mode",
        AxisX = "X",
        AxisY = "Y",
        SizeWidth = "Width",
        SizeHeight = "Height",
        NewVersionAvailable = "New version available",
        Close = "Close",
        LanguageEnglish = "English",
        LanguageKorean = "Korean",
        EditConfirmToolTip = "Confirm",
        EditCancelToolTip = "Cancel"
    };

    private static readonly LocalizedText KoreanText = new()
    {
        InputWindowTitle = "ExCord 입력",
        InputHeader = "메시지를 입력하세요",
        SettingsWindowTitle = "ExCord 설정",
        TabGeneral = "일반",
        TabTts = "TTS",
        TabGifTalk = "GifTalk",
        SectionApplication = "애플리케이션",
        SectionFeatures = "기능",
        SectionLanguage = "언어",
        StartWithWindows = "Windows 시작 시 실행",
        TextToSpeech = "텍스트 음성 변환",
        GifTalk = "GifTalk",
        GlobalHotkey = "전역 단축키",
        TtsVoice = "TTS 음성",
        VirtualOutputDevice = "가상 출력 장치",
        OutputVolume = "출력 볼륨",
        MonitorSound = "기본 스피커로 모니터 소리 재생",
        GifUrl = "GIF URL",
        Apply = "적용",
        EditMode = "편집 모드",
        AxisX = "X",
        AxisY = "Y",
        SizeWidth = "너비",
        SizeHeight = "높이",
        NewVersionAvailable = "새 버전이 있습니다",
        Close = "닫기",
        LanguageEnglish = "영어",
        LanguageKorean = "한국어",
        EditConfirmToolTip = "확인",
        EditCancelToolTip = "취소"
    };

    static LocalizationService()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ko", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Korean
            : AppLanguage.English;
        CurrentLanguage = language;
    }

    public static AppLanguage CurrentLanguage { get; private set; }

    public static LocalizedText Text => CurrentLanguage == AppLanguage.Korean ? KoreanText : EnglishText;

    public static void SetLanguage(AppLanguage language)
    {
        CurrentLanguage = language;
    }
}

public sealed class LocalizedText
{
    public required string InputWindowTitle { get; init; }
    public required string InputHeader { get; init; }
    public required string SettingsWindowTitle { get; init; }
    public required string TabGeneral { get; init; }
    public required string TabTts { get; init; }
    public required string TabGifTalk { get; init; }
    public required string SectionApplication { get; init; }
    public required string SectionFeatures { get; init; }
    public required string SectionLanguage { get; init; }
    public required string StartWithWindows { get; init; }
    public required string TextToSpeech { get; init; }
    public required string GifTalk { get; init; }
    public required string GlobalHotkey { get; init; }
    public required string TtsVoice { get; init; }
    public required string VirtualOutputDevice { get; init; }
    public required string OutputVolume { get; init; }
    public required string MonitorSound { get; init; }
    public required string GifUrl { get; init; }
    public required string Apply { get; init; }
    public required string EditMode { get; init; }
    public required string AxisX { get; init; }
    public required string AxisY { get; init; }
    public required string SizeWidth { get; init; }
    public required string SizeHeight { get; init; }
    public required string NewVersionAvailable { get; init; }
    public required string Close { get; init; }
    public required string LanguageEnglish { get; init; }
    public required string LanguageKorean { get; init; }
    public required string EditConfirmToolTip { get; init; }
    public required string EditCancelToolTip { get; init; }
}
