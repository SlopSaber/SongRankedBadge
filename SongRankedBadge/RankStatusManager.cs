using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using SongDetailsCache;
using SongDetailsCache.Structs;

namespace SongRankedBadge
{
    internal class RankStatusManager
    {
        internal static readonly RankStatusManager Instance = new RankStatusManager();
        
        private SongDetails? _songDetails = null;
        private Task<SongDetails>? _initialization;
        private bool _stopped;
        private bool _failureLogged;


        internal void Init()
        {
            if (_initialization != null || _stopped)
                return;

            Plugin.Log.Debug("Loading song details...");
            _initialization = Task.Run(InitializeSongDetails);
            _initialization.ContinueWith(ObserveFailure, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static async Task<SongDetails> InitializeSongDetails()
            => await SongDetails.Init().ConfigureAwait(false);

        private static void ObserveFailure(Task<SongDetails> initialization)
            => _ = initialization.Exception;

        internal void Stop()
        {
            _stopped = true;
            _songDetails = null;
        }

        private bool PublishInitialization()
        {
            if (_stopped)
                return false;
            if (_songDetails != null)
                return true;
            if (_initialization == null || !_initialization.IsCompleted)
                return false;
            if (_initialization.Status != TaskStatus.RanToCompletion)
            {
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    Plugin.Log.Warn("Unable to load song details.");
                    if (_initialization.Exception != null)
                        Plugin.Log.Debug(_initialization.Exception);
                }
                return false;
            }

            _songDetails = _initialization.GetAwaiter().GetResult();
            Plugin.Log.Debug("Song details loaded.");
            return true;
        }

        internal RankStatus GetSongRankedStatus(string hash)
        {
            if (!PublishInitialization())
            {
                // Data not ready yet
                return RankStatus.None;
            }
            
            hash = hash.ToLower();
            if (_songDetails!.songs.FindByHash(hash, out var song))
            {
                var rankedStates = song.rankedStates;
                var uploadFlags = song.uploadFlags;
                
                var ssRank = rankedStates.HasFlag(RankedStates.ScoresaberRanked);
                var blRank = rankedStates.HasFlag(RankedStates.BeatleaderRanked);
                var curated = uploadFlags.HasFlag(UploadFlags.Curated);
                if (ssRank && blRank)
                {
                    return RankStatus.Ranked;
                }

                if (blRank)
                {
                    return RankStatus.BeatLeader;
                }

                if (ssRank)
                {
                    return RankStatus.ScoreSaber;
                }
                
                if (curated)
                {
                    return RankStatus.Curated;
                }
            }

            return RankStatus.None;
        }
    }

    internal enum RankStatus
    {
        None,
        ScoreSaber,
        BeatLeader,
        Ranked, // just ranked, means both
        Curated  // curated comes after ranked status
    }
}
