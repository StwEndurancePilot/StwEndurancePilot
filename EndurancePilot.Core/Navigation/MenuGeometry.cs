namespace EndurancePilot.Core;

// Fortnite keeps some controls in a centered 16:9 area on wide displays, while
// zone arrows and footer actions stay pinned to the outer edges. All dimensions
// scale with client height so the same target works at different resolutions.
public static class MenuGeometry
{
    private enum HorizontalAnchor { Left, Center, Right }

    public static bool UsesFixedTarget(NavigationStep? step) => step is
        NavigationStep.OpenMap or NavigationStep.ChangeZone or NavigationStep.EnterZone
        or NavigationStep.SelectHomebase or NavigationStep.OpenHomebase or NavigationStep.SelectOwnHomebase
        or NavigationStep.LaunchHomebase or NavigationStep.LaunchLoadedHomebase
        or NavigationStep.SelectStormShieldTab or NavigationStep.SelectEndurance or NavigationStep.SelectStormShieldExpansion;

    public static Box Target(NavigationStep step, Observation frame, bool forward = true)
    {
        var (x, y, w, h, anchor) = step switch
        {
            NavigationStep.OpenMap => (.29, .08, .045, .04, HorizontalAnchor.Center),
            NavigationStep.ChangeZone when forward => (.073, .23, .018, .04, HorizontalAnchor.Left),
            NavigationStep.ChangeZone => (.048, .23, .018, .04, HorizontalAnchor.Left),
            NavigationStep.EnterZone or NavigationStep.OpenHomebase => (.78, .84, .14, .04, HorizontalAnchor.Right),
            NavigationStep.SelectHomebase when frame.Has(@"^PLA[YV] WITH OTHERS$") => (.61, .68, .045, .08, HorizontalAnchor.Center),
            NavigationStep.SelectHomebase => (.48, .58, .04, .08, HorizontalAnchor.Center),
            NavigationStep.SelectOwnHomebase => (.26, .26, .13, .04, HorizontalAnchor.Center),
            NavigationStep.LaunchHomebase => (.56, .86, .09, .04, HorizontalAnchor.Center),
            NavigationStep.LaunchLoadedHomebase => (.82, .80, .12, .07, HorizontalAnchor.Right),
            NavigationStep.SelectStormShieldTab => (.31, .02, .09, .035, HorizontalAnchor.Center),
            NavigationStep.SelectEndurance or NavigationStep.SelectStormShieldExpansion => (.24, .46, .12, .16, HorizontalAnchor.Center),
            NavigationStep.DisableAssistance or NavigationStep.EnableAssistance => (.44, .59, .02, .045, HorizontalAnchor.Center),
            NavigationStep.StartEndurance => (.43, .70, .14, .04, HorizontalAnchor.Center),
            _ => throw new ArgumentOutOfRangeException(nameof(step))
        };
        double uiWidth = Math.Min(frame.Width, frame.Height * 16d / 9);
        double offset = anchor switch
        {
            HorizontalAnchor.Left => 0,
            HorizontalAnchor.Center => (frame.Width - uiWidth) / 2,
            HorizontalAnchor.Right => frame.Width - uiWidth,
            _ => throw new ArgumentOutOfRangeException(nameof(anchor))
        };
        return new Box(offset + x * uiWidth, y * frame.Height, w * uiWidth, h * frame.Height);
    }
}
