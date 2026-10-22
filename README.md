# Endurance Pilot

Endurance Pilot is a Windows app for running Fortnite **Save the World Storm Shield Endurance**. It navigates to your chosen Homebase, starts Endurance, tracks confirmed waves, collects mission rewards, and can repeat runs. It is early-access software, so check on a run if the game shows an unfamiliar screen.

![Endurance Pilot dashboard](docs/images/endurance-pilot-ui.png)

*Screenshot from an earlier build. The current dashboard shows completed runs inside the earnings view and resets the wave display to 00 after a run.*

## Getting started

1. Install the latest Windows setup from [Releases](https://github.com/StwEndurancePilot/StwEndurancePilot/releases). The installer checks for the .NET 10 Windows Desktop Runtime.
2. Open Fortnite through the Epic Games Launcher, or enable **Launch and restart Fortnite** in Endurance Pilot's settings.
3. Choose your **Homebase**. In **Settings**, set the target wave, whether to repeat indefinitely or stop after a chosen number of attempts, and whether to wait for assistance.
4. Select **Start endurance** or press the start/stop hotkey (F10 by default). Keep Fortnite focused while the runner is active. If you take control, use **Resume in Fortnite** when prompted.
5. Watch wave progress and review rewards under **This session**, **Latest mission**, or **Lifetime earnings**. Select **Stop runner** to end the session.

Settings also include an idle delay for refocusing Fortnite, a configurable hotkey, and optional Discord webhook notifications.

