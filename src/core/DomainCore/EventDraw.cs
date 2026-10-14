using System;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// 1 年のイベントの抽選結果（requirements.md 4.2-3：0 件か 1 件）。
    /// HasEvent が false のときに Event を読むと InvalidOperationException（既定値は返さない）。
    /// </summary>
    public sealed class EventDraw
    {
        private readonly LifeEvent lifeEvent;

        private EventDraw(LifeEvent lifeEvent)
        {
            this.lifeEvent = lifeEvent;
        }

        /// <summary>イベントが発生したら true。</summary>
        public bool HasEvent => lifeEvent != null;

        /// <summary>発生したイベント。HasEvent が false なら InvalidOperationException。</summary>
        public LifeEvent Event => HasEvent ? lifeEvent : throw new InvalidOperationException(ErrorMessages.NoEventDrawn);

        /// <summary>この年はイベントが発生しなかった。</summary>
        public static EventDraw None() => new EventDraw(null);

        /// <summary>このイベントが発生した。</summary>
        public static EventDraw Of(LifeEvent lifeEvent) => new EventDraw(lifeEvent ?? throw new ArgumentNullException(nameof(lifeEvent)));
    }
}
