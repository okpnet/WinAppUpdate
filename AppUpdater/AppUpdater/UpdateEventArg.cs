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
        internal UpdateState State { get; }

        public bool IsCheckFinished { get; }

        public bool IsCancel { get; set; }

        public FileInfo? DownLoadFilePath { get; }

        public string Version { get; }

        public UpdateEventArg(UpdateState state, bool isCheckFinished, bool isCancel, FileInfo? downloadFilePath, string version)
        {
            State = state;
            IsCheckFinished = isCheckFinished;
            IsCancel = isCancel;
            DownLoadFilePath = downloadFilePath;
            Version = version;
        }

        public static UpdateEventArg StandbyUpdate(FileInfo downloadFilPath, string version) => new UpdateEventArg(UpdateState.UpdateStandby, false, false, downloadFilPath, version);

        public static UpdateEventArg AvailableUpdate() => new UpdateEventArg(UpdateState.UpdateAvailable, true, false, null, string.Empty);

        public static UpdateEventArg NotAvailableUpdate() => new UpdateEventArg(UpdateState.UpdateNotAvailable, true, false, null, string.Empty);


    }
}
