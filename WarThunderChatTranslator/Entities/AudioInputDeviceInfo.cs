namespace WarThunderChatTranslator.Entities
{
    public sealed class AudioInputDeviceInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool IsDefault { get; init; }

        public override string ToString() => Name;
    }
}
