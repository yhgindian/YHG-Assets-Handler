using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace YHG_Assets_Handler.Draft
{
    public abstract class BaseDraftManager
    {
        public abstract Task<bool> SaveDraft(string draftFilePath, CancellationToken token = default);

        public abstract Task<bool> LoadDraft(string draftFilePath, CancellationToken token = default);
    }
}