using UnityEngine;

namespace RusK.Mods.Shared;

/// <summary>
/// 今操作しているプレイヤー。ゲーム自身の GameUtil.GetPlayer() を使う。
/// (FindObjectOfType だと、キャラ切り替えで控えにいるキャラを掴んでしまうことがある)
/// 1 フレームに 1 回だけ取り直す。
/// </summary>
internal static class PlayerRef
{
    private static PlayerController _cached;
    private static int _frame = -1;

    public static PlayerController Current
    {
        get
        {
            if (_frame == Time.frameCount && _cached != null) return _cached;
            _frame = Time.frameCount;

            PlayerController player = null;
            try
            {
                var util = GameUtil.Instance;
                if (util != null)
                {
                    player = util.GetPlayer();
                    if (player == null) player = util.m_curPlayer;
                }
            }
            catch { }

            // ゲームから取れないとき (タイトル画面など) だけシーンから探す
            if (player == null && _cached == null)
                player = UnityEngine.Object.FindObjectOfType<PlayerController>();

            if (player != null) _cached = player;
            return _cached != null ? _cached : null; // 破棄済みなら Unity の == で null 扱い
        }
    }
}
