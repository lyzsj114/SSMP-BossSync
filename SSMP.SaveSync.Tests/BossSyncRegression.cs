using System.Reflection;
using SSMP.Game.Client.Entity.Action;
using SSMP.Game.Client.Save;
using SSMP.Game.Server;

internal static class BossSyncRegression {
    internal static void Run(Action<bool, string> check) {
        check(
            SceneHostPolicy.ShouldAssignEnteringPlayerAsHost(false, false),
            "First player entering an empty scene becomes scene host"
        );
        check(
            !SceneHostPolicy.ShouldAssignEnteringPlayerAsHost(true, true),
            "Joining an owned scene does not steal scene host"
        );
        check(
            SceneHostPolicy.ShouldAssignEnteringPlayerAsHost(true, false),
            "Missing scene host is repaired by the next entering player"
        );

        var suppressionDepthField = typeof(FsmActionHooks).GetField(
            "_setParentSuppressionDepth",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        var baselineDepth = (int) suppressionDepthField.GetValue(null)!;
        using (FsmActionHooks.SuppressSetParentActions()) {
            check(
                (int) suppressionDepthField.GetValue(null)! == baselineDepth + 1,
                "Entity cloning suppresses unsafe SetParent actions"
            );
            using (FsmActionHooks.SuppressSetParentActions()) {
                check(
                    (int) suppressionDepthField.GetValue(null)! == baselineDepth + 2,
                    "Nested entity cloning keeps SetParent suppression active"
                );
            }

            check(
                (int) suppressionDepthField.GetValue(null)! == baselineDepth + 1,
                "Nested SetParent suppression unwinds exactly one level"
            );
        }

        check(
            (int) suppressionDepthField.GetValue(null)! == baselineDepth,
            "SetParent suppression is restored after entity cloning"
        );

        foreach (var fieldName in new[] { "FisherWalkerIdleTimeLeft", "FisherWalkerTimer" }) {
            check(
                !SaveDataMapping.Instance.PlayerDataVarProperties[fieldName].Sync,
                $"Frame timer {fieldName} stays local and does not flood the network"
            );
        }
    }
}
