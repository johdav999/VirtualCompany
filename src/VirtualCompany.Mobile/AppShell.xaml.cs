using VirtualCompany.Shared.Mobile;

﻿namespace VirtualCompany.Mobile;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
        Navigating += OnNavigating;
	}

    private static void OnNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        var targetRoute = e.Target.Location.OriginalString;
        if (MobileCompanionScope.IsRouteSupported(targetRoute))
        {
            return;
        }

        e.Cancel();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Current.DisplayAlert("Available on web", MobileCompanionScope.WebFirstAdministrationMessage, "OK");
        });
    }
}
