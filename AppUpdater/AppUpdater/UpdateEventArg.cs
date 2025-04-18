using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppUpdater
{
    public sealed class UpdateEventArg: ICancelEventArg
    {
        /// <summary>
        /// 待機タスク
        /// </summary>
        TaskCompletionSource? _trigger = new();
        /// <summary>
        /// 更新の状態
        /// </summary>
        internal UpdateState State { get; }
        /// <summary>
        /// 継続をキャンセル。キャンセルするときTrue。
        /// </summary>
        public bool IsCancel { get; set; }
        /// <summary>
        /// ダウンロードした更新ファイルパス。URLに基づいてリネームしている。
        /// </summary>
        public FileInfo? DownLoadFilePath { get; }
        /// <summary>
        /// バージョン
        /// </summary>
        public string Version { get; }
        /// <summary>
        /// 待機タスク
        /// </summary>
        internal Task UpdateTriggerTask => _trigger?.Task ?? Task.CompletedTask;
        /// <summary>
        /// 更新があるときに、ユーザー側へ判断を委ねる。Trueで待機。
        /// </summary>
        public bool ShouldWait 
        {
            get=>_trigger is not null;
            set
            {
                _trigger = value ? new() : null;
            }
        }

        public UpdateEventArg(UpdateState state, bool isCancel, FileInfo? downloadFilePath, string version)
        {
            State = state;
            IsCancel = isCancel;
            DownLoadFilePath = downloadFilePath;
            Version = version;
        }
        /// <summary>
        /// 待機完了。Trueで更新を開始、Falseで更新キャンセル。
        /// </summary>
        public void TriggerUpdate(bool isContinueForUpdate)
        {
            if(isContinueForUpdate)
            {
                _trigger?.TrySetResult(); 
            }
            else
            {
                _trigger?.TrySetCanceled(); 
            }
        }

        /// <summary>
        /// ファクトリ
        /// 更新ファイルダウンロード完了後、待機している
        /// </summary>
        /// <param name="downloadFilPath"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        public static UpdateEventArg StandbyUpdate(FileInfo downloadFilPath, string version) => new UpdateEventArg(UpdateState.UpdateStandby, false, downloadFilPath, version);
        /// <summary>
        /// ファクトリ
        /// 更新があった
        /// </summary>
        /// <returns></returns>
        public static UpdateEventArg AvailableUpdate(string version) => new UpdateEventArg(UpdateState.UpdateAvailable, false, null, version);
        /// <summary>
        /// ファクトリ
        /// 更新がなかった
        /// </summary>
        /// <returns></returns>
        public static UpdateEventArg NotAvailableUpdate() => new UpdateEventArg(UpdateState.UpdateNotAvailable, false, null, string.Empty);
    }
}
