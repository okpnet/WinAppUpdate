using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppUpdater
{
    public class UpdateProgressEventArg: ICancelEventArg
    {
        public bool IsCancel { get; set; }

        public int ProgressPercent { get; }

        public DownloadState DownloadState { get; }

        public Exception? DownloadException { get; }

        public UpdateProgressEventArg(int progressPercent, DownloadState downloadState , Exception? exception=null)
        {
            ProgressPercent = progressPercent;
            DownloadState = downloadState;
            DownloadException = exception;
        }
    }
}
