using System;
using System.Collections.Generic;
using LifeAllocationSim.Config;

namespace LifeAllocationSim.Ui
{
    /// <summary>ボタンに対応する操作（ラベルと、押したときの処理）。</summary>
    public sealed class MessageAction
    {
        public MessageAction(string label, Action invoke)
        {
            Label = label;
            Invoke = invoke;
        }

        public string Label { get; }

        public Action Invoke { get; }
    }

    /// <summary>お知らせ・確認・エラーの画面。見出し・文・ボタンだけを並べる。</summary>
    public sealed class MessageScreen
    {
        private readonly ScreenHost host;

        public MessageScreen(ScreenHost host)
        {
            this.host = host;
        }

        public void Show(string heading, string message, IReadOnlyList<MessageAction> actions)
        {
            var factory = host.Factory;
            var content = host.BeginScreen();
            factory.AddHeading(content, heading);
            factory.AddText(content, message, AppConfig.BodyFontSize);
            foreach (var action in actions)
            {
                factory.AddButton(content, action.Label, action.Invoke);
            }
        }
    }
}
