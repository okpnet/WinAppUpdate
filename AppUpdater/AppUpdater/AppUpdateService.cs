using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.SignatureVerifiers;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace AppUpdater
{
    public class AppUpdateService
    {
        readonly SparkleUpdater _sparkle = default!;
        readonly ILogger? _logger;
        readonly Subject<UpdateEventArg> _subject = new();
        /// <summary>
        /// イベントハンドラDispose
        /// </summary>
        CompositeDisposable _disposables = new CompositeDisposable();
        /// <summary>
        /// AppcastURL
        /// </summary>
        Uri _appcastUrl = default!;
        /// <summary>
        /// 公開鍵パス
        /// </summary>
        FileInfo _publicKeyPath = default!;
        /// <summary>
        /// ダウンロードしたアップデートファイル
        /// </summary>
        FileInfo? _downloadFile;
        /// <summary>
        /// Sparkleインスタンス
        /// </summary>
        public SparkleUpdater Sparkle { get => _sparkle; }
        /// <summary>
        /// クローズアクション
        /// </summary>
        public Action? AppCloseAction { get; init; }
        /// <summary>
        /// 準備完了
        /// </summary>
        public bool UpdateReady { get; protected set; }
        /// <summary>
        /// 準備完了通知
        /// </summary>
        public IObservable<UpdateEventArg> UpdateStandbyEvent { get; }
        /// <summary>
        /// アップデートステータスチェックイベント
        /// </summary>
        public IObservable<UpdateEventArg> UpdateCheckFinishedEvent { get; }

        public static AppUpdateService CreateAppUpdateService(string publicKey, Uri appcastUrl, Action? appclose,ILogger<AppUpdateService>? logger=null)
        {
            var key=new Ed25519Checker(SecurityMode.Unsafe, publicKey);
            return new AppUpdateService(key, appcastUrl, appclose, logger);
        }

        public static AppUpdateService CreateAppUpdateService(FileInfo publicKeyFile, Uri appcastUrl, Action? appclose, ILogger<AppUpdateService>? logger = null)
        {
            if (!publicKeyFile.Exists)
            {
                throw new FileNotFoundException($"publicKeyFile '{publicKeyFile.Name}' is not found.");
            }
            var key = new Ed25519Checker(SecurityMode.Unsafe, null , publicKeyFile.FullName);
            return new AppUpdateService(key, appcastUrl, appclose, logger);
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        private AppUpdateService(Ed25519Checker publicKey, Uri appcastUrl,Action? appclose,ILogger? logger)
        {

            _appcastUrl = appcastUrl;
            AppCloseAction = appclose;
            UpdateReady = false;
            _logger = logger;
            _sparkle = new SparkleUpdater
                (
                    _appcastUrl.AbsoluteUri,
                    publicKey
                )
            {
                UIFactory = null,
                RelaunchAfterUpdate = false
            };

            UpdateStandbyEvent = _subject.Where(t => t.State == UpdateState.UpdateStandby).AsObservable();//準備完了イベント
            UpdateCheckFinishedEvent = _subject.Where(t => t.IsCheckFinished).AsObservable();//チェック完了イベント

            AddEvent();

            _sparkle.CheckForUpdatesQuietly();
        }
        /// <summary>
        /// イベント追加
        /// </summary>
        private void AddEvent()
        {
            _disposables.Add(
                Observable.FromEventPattern<UpdateDetectedEventArgs>(_sparkle,nameof(_sparkle.UpdateDetected)).Subscribe(e=>
                    {//アップデートが見つかったとき
                        var arg = UpdateEventArg.AvailableUpdate(); 
                        _subject.OnNext(arg);
                        e.EventArgs.NextAction = NextUpdateAction.PerformUpdateUnattended;
                    })
                );

            _disposables.Add(//チェック完了イベント
                Observable.FromEventPattern<object, UpdateStatus>(_sparkle, nameof(_sparkle.UpdateCheckFinished))
                .Subscribe(async (t) =>
                {
                    var updateInfo = await _sparkle.CheckForUpdatesQuietly();
                    if (updateInfo is null || !updateInfo.Updates.Any()) 
                    {
                        return;
                    }

                    var arg = t.EventArgs == UpdateStatus.UpdateAvailable ?
                            UpdateEventArg.AvailableUpdate() : UpdateEventArg.NotAvailableUpdate();
                    _subject.OnNext(arg);
                    if (t.EventArgs != UpdateStatus.UpdateAvailable || arg.IsCancel)
                    {
                        return;
                    }

                    //var updateDate = updateInfo.Updates.Last();
                    //await _sparkle.InitAndBeginDownload(updateDate);
                })
            );

            _disposables.Add(//準備完了イベント
                Observable.FromEventPattern<object, string>(_sparkle, nameof(_sparkle.DownloadFinished)).
                Subscribe(async (t) =>
                {
                    var updateInfo = await _sparkle.CheckForUpdatesQuietly();
                    if (_sparkle is null || updateInfo is null || !updateInfo.Updates.Any())
                    {
                        return;
                    }

                    var updater = updateInfo.Updates.Last();
                    if (updater is null) 
                    {
                        return;
                    }

                    _downloadFile = new(t.EventArgs);
                    _subject.OnNext(UpdateEventArg.StandbyUpdate(_downloadFile, updater.Version??""));
                    UpdateReady = true;
                })
            );

            if(AppCloseAction is not null)
            {
                _disposables.Add(//終了イベント
                    Observable.FromEvent<CloseApplication, Unit>(
                        handler => () => handler(Unit.Default),
                        h => _sparkle.CloseApplication += h,
                        h => _sparkle.CloseApplication -= h).
                        Subscribe(_ => AppCloseAction.Invoke())
                    );
            }

        }
       
        public async Task UpdateDitectAsync()
        {
            var updateInfo=await _sparkle.CheckForUpdatesQuietly();
            if(updateInfo is null || !updateInfo.Updates.Any())
            {
                return;
            }

        }

        /// <summary>
        /// アップデート実行
        /// </summary>
        public async Task UpdateAsync(Action<UpdateEventArg> action)
        {
            var updateInfo = await _sparkle.CheckForUpdatesQuietly();
            if (_sparkle is null && updateInfo is null)
            {
                throw new NullReferenceException($"NetSparkleUpdater requierd for update is null.");
            }
            if (!updateInfo.Updates.Any())
            {
                UpdateEventArg.NotAvailableUpdate();
                return;
            }
            var updateDate = updateInfo.Updates.First();
            var arg = _downloadFile is null ?
                UpdateEventArg.NotAvailableUpdate() : UpdateEventArg.StandbyUpdate(_downloadFile, updateDate.Version??"");

            action(arg);
            if (arg.IsCancel)
            {
                return;
            }
            await _sparkle.InstallUpdate(updateDate);
        }
        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            _disposables.Clear();
        }
    }
}
