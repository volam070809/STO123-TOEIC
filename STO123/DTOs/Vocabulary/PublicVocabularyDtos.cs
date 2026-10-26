namespace STO123.DTOs.Vocabulary;

public sealed record PublicVocabularyTopicDto(
    int MaChuDe,
    string TenChuDe,
    string? MoTa,
    int TotalWords,
    int PreviewWords);

public sealed record PublicVocabularyWordDto(
    int MaTuVung,
    string Word,
    string? Pronunciation,
    string? PartOfSpeech,
    string Meaning,
    string? Example,
    string? ExampleMeaning,
    bool HasAudio,
    bool HasExampleAudio);

public sealed record PublicVocabularyPreviewDto(
    PublicVocabularyTopicDto Topic,
    IReadOnlyList<PublicVocabularyWordDto> Words);
