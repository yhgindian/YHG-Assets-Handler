namespace AssetsHandler.Controls
{
    public record FindResult(int CurrentIndex, int TotalMatches, bool HasMatches);
    public record FindOptions(string Query, bool MatchCase, bool WholeWord, bool UseRegex, bool Forward);
    public interface IFindTarget
    {
        FindResult Find(FindOptions options);
        void OnFindClosed() { }
    }
}