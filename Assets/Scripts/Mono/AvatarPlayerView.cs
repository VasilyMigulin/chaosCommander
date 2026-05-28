using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// Заглушка аватара игрока на доске.
    /// Визуальный скин назначается позже (донатная система).
    /// </summary>
    public class AvatarPlayerView : MonoBehaviour
    {
        public int OwnerId { get; private set; }

        public void Init(int ownerId)
        {
            OwnerId = ownerId;
            gameObject.name = $"AvatarPlayer_P{ownerId}";
        }
    }
}
