﻿using ApplicationServices;
using AudibleUtilities;
using Avalonia.Controls;
using Dinah.Core;
using LibationFileManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibationWinForms.AvaloniaUI.Views
{
	//DONE
	public partial class MainWindow
	{
		private InterruptableTimer autoScanTimer;

		private void Configure_ScanAuto()
		{
			// creating InterruptableTimer inside 'Configure_' is a break from the pattern. As long as no one else needs to access or subscribe to it, this is ok
			var hours = 0;
			var minutes = 5;
			var seconds = 0;
			var _5_minutes = new TimeSpan(hours, minutes, seconds);
			autoScanTimer = new InterruptableTimer(_5_minutes);

			// subscribe as async/non-blocking. I'd actually rather prefer blocking but real-world testing found that caused a deadlock in the AudibleAPI
			autoScanTimer.Elapsed += async (_, __) =>
			{
				using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
				var accounts = persister.AccountsSettings
					.GetAll()
					.Where(a => a.LibraryScan)
					.ToArray();

				// in autoScan, new books SHALL NOT show dialog
				try
				{
					await LibraryCommands.ImportAccountAsync(Login.WinformLoginChoiceEager.ApiExtendedFunc, accounts);
				}
				catch (Exception ex)
				{
					Serilog.Log.Logger.Error(ex, "Error invoking auto-scan");
				}
			};

			// load init state to menu checkbox
			Opened += updateAutoScanLibraryToolStripMenuItem;
			// if enabled: begin on load
			Opened += startAutoScan;

			// if new 'default' account is added, run autoscan
			AccountsSettingsPersister.Saving += accountsPreSave;
			AccountsSettingsPersister.Saved += accountsPostSave;

			// when autoscan setting is changed, update menu checkbox and run autoscan
			Configuration.Instance.AutoScanChanged += updateAutoScanLibraryToolStripMenuItem;
			Configuration.Instance.AutoScanChanged += startAutoScan;
		}

		private List<(string AccountId, string LocaleName)> preSaveDefaultAccounts;
		private List<(string AccountId, string LocaleName)> getDefaultAccounts()
		{
			using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
			return persister.AccountsSettings
				.GetAll()
				.Where(a => a.LibraryScan)
				.Select(a => (a.AccountId, a.Locale.Name))
				.ToList();
		}
		private void accountsPreSave(object sender = null, EventArgs e = null)
			=> preSaveDefaultAccounts = getDefaultAccounts();
		private void accountsPostSave(object sender = null, EventArgs e = null)
		{
			var postSaveDefaultAccounts = getDefaultAccounts();
			var newDefaultAccounts = postSaveDefaultAccounts.Except(preSaveDefaultAccounts).ToList();

			if (newDefaultAccounts.Any())
				startAutoScan();
		}

		private void startAutoScan(object sender = null, EventArgs e = null)
		{
			if (Configuration.Instance.AutoScan)
				autoScanTimer.PerformNow();
			else
				autoScanTimer.Stop();
		}
		private void updateAutoScanLibraryToolStripMenuItem(object sender, EventArgs e) => autoScanLibraryToolStripMenuItemCheckbox.IsChecked = Configuration.Instance.AutoScan;
		private void autoScanLibraryToolStripMenuItem_Click(object sender, Avalonia.Interactivity.RoutedEventArgs args) => Configuration.Instance.AutoScan = autoScanLibraryToolStripMenuItemCheckbox.IsChecked != true;
	}
}
