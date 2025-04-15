using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppUpdater
{
    public class UpdateProgressEventArg: ICancelEventArg
    {
        /// <summary>
        /// 継続をキャンセル。キャンセルするときTrue。
        /// </summary>
        public bool IsCancel { get; set; }
        /// <summary>
        /// 進捗率
        /// </summary>
        public int ProgressPercent { get; }
        /// <summary>
        /// ダウンロードの状態
        /// </summary>
        public DownloadState DownloadState { get; }
        /// <summary>
        /// ダウンロード開始から発生した例外
        /// DownloadStateがErrorのときに値がある
        /// </summary>
        public Exception? DownloadException { get; }

        public UpdateProgressEventArg(int progressPercent, DownloadState downloadState , Exception? exception=null)
        {
            ProgressPercent = progressPercent;
            DownloadState = downloadState;
            DownloadException = exception;
        }
    }
}
