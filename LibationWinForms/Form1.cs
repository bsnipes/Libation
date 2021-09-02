﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ApplicationServices;
using DataLayer;
using Dinah.Core;
using Dinah.Core.Drawing;
using Dinah.Core.Windows.Forms;
using FileManager;
using InternalUtilities;
using LibationWinForms.Dialogs;

namespace LibationWinForms
{
	public partial class Form1 : Form
	{
		private string beginBookBackupsToolStripMenuItem_format { get; }
		private string beginPdfBackupsToolStripMenuItem_format { get; }

		public Form1()
		{
			InitializeComponent();

			// back up string formats
			beginBookBackupsToolStripMenuItem_format = beginBookBackupsToolStripMenuItem.Text;
			beginPdfBackupsToolStripMenuItem_format = beginPdfBackupsToolStripMenuItem.Text;

			if (this.DesignMode)
				return;

			// independent UI updates
			this.Load += (_, __) => RestoreSizeAndLocation();
			this.Load += (_, __) => RefreshImportMenu();
			LibraryCommands.BookUserDefinedItemCommitted += setBackupCounts;

			var format = System.Drawing.Imaging.ImageFormat.Jpeg;
			PictureStorage.SetDefaultImage(PictureSize._80x80, Properties.Resources.default_cover_80x80.ToBytes(format));
			PictureStorage.SetDefaultImage(PictureSize._300x300, Properties.Resources.default_cover_300x300.ToBytes(format));
			PictureStorage.SetDefaultImage(PictureSize._500x500, Properties.Resources.default_cover_500x500.ToBytes(format));
		}

		private void Form1_Load(object sender, EventArgs e)
		{
			if (this.DesignMode)
				return;

			reloadGridAndUpdateBottomNumbers();

			// also applies filter. ONLY call AFTER loading grid
			loadInitialQuickFilterState();
		}

		private void Form1_FormClosing(object sender, FormClosingEventArgs e)
		{
			SaveSizeAndLocation();
		}

		private void RestoreSizeAndLocation()
		{
			var config = Configuration.Instance;

			var width = config.MainFormWidth;
			var height = config.MainFormHeight;

			// too small -- something must have gone wrong. use defaults
			if (width < 25 || height < 25)
			{
				width = 1023;
				height = 578;
			}

			// Fit to the current screen size in case the screen resolution changed since the size was last persisted
			if (width > Screen.PrimaryScreen.WorkingArea.Width)
				width = Screen.PrimaryScreen.WorkingArea.Width;
			if (height > Screen.PrimaryScreen.WorkingArea.Height)
				height = Screen.PrimaryScreen.WorkingArea.Height;

			var x = config.MainFormX;
			var y = config.MainFormY;

			var rect = new System.Drawing.Rectangle(x, y, width, height);

			// is proposed rect on a screen?
			if (Screen.AllScreens.Any(screen => screen.WorkingArea.Contains(rect)))
			{
				this.StartPosition = FormStartPosition.Manual;
				this.DesktopBounds = rect;
			}
			else
			{
				this.StartPosition = FormStartPosition.WindowsDefaultLocation;
				this.Size = rect.Size;
			}

			// FINAL: for Maximized: start normal state, set size and location, THEN set max state
			this.WindowState = config.MainFormIsMaximized ? FormWindowState.Maximized : FormWindowState.Normal;
		}

		private void SaveSizeAndLocation()
		{
			System.Drawing.Point location;
			System.Drawing.Size size;

			// save location and size if the state is normal
			if (this.WindowState == FormWindowState.Normal)
			{
				location = this.Location;
				size = this.Size;
			}
			else
			{
				// save the RestoreBounds if the form is minimized or maximized
				location = this.RestoreBounds.Location;
				size = this.RestoreBounds.Size;
			}

			var config = Configuration.Instance;

			config.MainFormX = location.X;
			config.MainFormY = location.Y;

			config.MainFormWidth = size.Width;
			config.MainFormHeight = size.Height;

			config.MainFormIsMaximized = this.WindowState == FormWindowState.Maximized;
		}

		private void reloadGridAndUpdateBottomNumbers()
		{
			// suppressed filter while init'ing UI
			var prev_isProcessingGridSelect = isProcessingGridSelect;
			isProcessingGridSelect = true;
			setGrid();
			isProcessingGridSelect = prev_isProcessingGridSelect;

			// UI init complete. now we can apply filter
			doFilter(lastGoodFilter);

			setBackupCounts(null, null);
		}

		#region reload grid
		private ProductsGrid currProductsGrid;
		private void setGrid()
		{
			SuspendLayout();
			{
				if (currProductsGrid != null)
				{
					gridPanel.Controls.Remove(currProductsGrid);
					currProductsGrid.VisibleCountChanged -= setVisibleCount;
					currProductsGrid.Dispose();
				}

				currProductsGrid = new ProductsGrid { Dock = DockStyle.Fill };
				currProductsGrid.VisibleCountChanged += setVisibleCount;
				gridPanel.UIThread(() => gridPanel.Controls.Add(currProductsGrid));
				currProductsGrid.Display();
			}
			ResumeLayout();
		}
		#endregion

		#region bottom: qty books visible
		private void setVisibleCount(object _, int qty) => visibleCountLbl.Text = string.Format("Visible: {0}", qty);
		#endregion

		#region bottom: backup counts
		private System.ComponentModel.BackgroundWorker updateCountsBw;
		private bool runBackupCountsAgain;

		private void setBackupCounts(object _, object __)
		{
			runBackupCountsAgain = true;

			if (updateCountsBw is not null)
				return;

			updateCountsBw = new System.ComponentModel.BackgroundWorker();
			updateCountsBw.DoWork += UpdateCountsBw_DoWork;
			updateCountsBw.RunWorkerCompleted += UpdateCountsBw_RunWorkerCompleted;
			updateCountsBw.RunWorkerAsync();
		}

		private void UpdateCountsBw_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
		{
			while (runBackupCountsAgain)
			{
				runBackupCountsAgain = false;

				var libraryStats = LibraryCommands.GetCounts();
				e.Result = libraryStats;
			}
			updateCountsBw = null;
		}

		private void UpdateCountsBw_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
		{
			var libraryStats = e.Result as LibraryCommands.LibraryStats;

			setBookBackupCounts(libraryStats);
			setPdfBackupCounts(libraryStats);
		}

		private void setBookBackupCounts(LibraryCommands.LibraryStats libraryStats)
		{
			var backupsCountsLbl_Format = "BACKUPS: No progress: {0}  Encrypted: {1}  Fully backed up: {2}";

			// enable/disable export
			var hasResults = 0 < (libraryStats.booksFullyBackedUp + libraryStats.booksDownloadedOnly + libraryStats.booksNoProgress + libraryStats.booksError);
			exportLibraryToolStripMenuItem.Enabled = hasResults;

			// update bottom numbers
			var pending = libraryStats.booksNoProgress + libraryStats.booksDownloadedOnly;
			var statusStripText
				= !hasResults ? "No books. Begin by importing your library"
				: libraryStats.booksError > 0 ? string.Format(backupsCountsLbl_Format + "  Errors: {3}", libraryStats.booksNoProgress, libraryStats.booksDownloadedOnly, libraryStats.booksFullyBackedUp, libraryStats.booksError)
				: pending > 0 ? string.Format(backupsCountsLbl_Format, libraryStats.booksNoProgress, libraryStats.booksDownloadedOnly, libraryStats.booksFullyBackedUp)
				: $"All {"book".PluralizeWithCount(libraryStats.booksFullyBackedUp)} backed up";

			// update menu item
			var menuItemText
				= pending > 0
				? $"{pending} remaining"
				: "All books have been liberated";

			// update UI
			statusStrip1.UIThread(() => backupsCountsLbl.Text = statusStripText);
			menuStrip1.UIThread(() => beginBookBackupsToolStripMenuItem.Enabled = pending > 0);
			menuStrip1.UIThread(() => beginBookBackupsToolStripMenuItem.Text = string.Format(beginBookBackupsToolStripMenuItem_format, menuItemText));
		}
		private void setPdfBackupCounts(LibraryCommands.LibraryStats libraryStats)
		{
			var pdfsCountsLbl_Format = "|  PDFs: NOT d/l\'ed: {0}  Downloaded: {1}";

			// update bottom numbers
			var hasResults = 0 < (libraryStats.pdfsNotDownloaded + libraryStats.pdfsDownloaded);
			var statusStripText
				= !hasResults ? ""
				: libraryStats.pdfsNotDownloaded > 0 ? string.Format(pdfsCountsLbl_Format, libraryStats.pdfsNotDownloaded, libraryStats.pdfsDownloaded)
				: $"|  All {libraryStats.pdfsDownloaded} PDFs downloaded";

			// update menu item
			var menuItemText
				= libraryStats.pdfsNotDownloaded > 0
				? $"{libraryStats.pdfsNotDownloaded} remaining"
				: "All PDFs have been downloaded";

			// update UI
			statusStrip1.UIThread(() => pdfsCountsLbl.Text = statusStripText);
			menuStrip1.UIThread(() => beginPdfBackupsToolStripMenuItem.Enabled = libraryStats.pdfsNotDownloaded > 0);
			menuStrip1.UIThread(() => beginPdfBackupsToolStripMenuItem.Text = string.Format(beginPdfBackupsToolStripMenuItem_format, menuItemText));
		}
		#endregion

		#region filter
		private void filterHelpBtn_Click(object sender, EventArgs e) => new SearchSyntaxDialog().ShowDialog();

		private void AddFilterBtn_Click(object sender, EventArgs e)
		{
			QuickFilters.Add(this.filterSearchTb.Text);
			UpdateFilterDropDown();
		}

		private void filterSearchTb_KeyPress(object sender, KeyPressEventArgs e)
		{
			if (e.KeyChar == (char)Keys.Return)
			{
				doFilter();

				// silence the 'ding'
				e.Handled = true;
			}
		}
		private void filterBtn_Click(object sender, EventArgs e) => doFilter();

		private bool isProcessingGridSelect = false;
		private string lastGoodFilter = "";
		private void doFilter(string filterString)
		{
			this.filterSearchTb.Text = filterString;
			doFilter();
		}
		private void doFilter()
		{
			if (isProcessingGridSelect || currProductsGrid == null)
				return;

			try
			{
				currProductsGrid.Filter(filterSearchTb.Text);
				lastGoodFilter = filterSearchTb.Text;
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Bad filter string:\r\n\r\n{ex.Message}", "Bad filter string", MessageBoxButtons.OK, MessageBoxIcon.Error);

				// re-apply last good filter
				doFilter(lastGoodFilter);
			}
		}
		#endregion

		#region Import menu
		public void RefreshImportMenu()
		{
			using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
			var count = persister.AccountsSettings.Accounts.Count;

			noAccountsYetAddAccountToolStripMenuItem.Visible = count == 0;
			scanLibraryToolStripMenuItem.Visible = count == 1;
			scanLibraryOfAllAccountsToolStripMenuItem.Visible = count > 1;
			scanLibraryOfSomeAccountsToolStripMenuItem.Visible = count > 1;

			removeLibraryBooksToolStripMenuItem.Visible = count != 0;

			removeSomeAccountsToolStripMenuItem.Visible = count > 1;
			removeAllAccountsToolStripMenuItem.Visible = count > 1;
		}

		private void noAccountsYetAddAccountToolStripMenuItem_Click(object sender, EventArgs e)
		{
			MessageBox.Show("To load your Audible library, come back here to the Import menu after adding your account");
			new AccountsDialog(this).ShowDialog();
		}

		private void scanLibraryToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
			var firstAccount = persister.AccountsSettings.GetAll().FirstOrDefault();
			scanLibraries(firstAccount);
		}

		private void scanLibraryOfAllAccountsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
			var allAccounts = persister.AccountsSettings.GetAll();
			scanLibraries(allAccounts);
		}

		private void scanLibraryOfSomeAccountsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using var scanAccountsDialog = new ScanAccountsDialog(this);

			if (scanAccountsDialog.ShowDialog() != DialogResult.OK)
				return;

			if (!scanAccountsDialog.CheckedAccounts.Any())
				return;

			scanLibraries(scanAccountsDialog.CheckedAccounts);
		}

		private void removeLibraryBooksToolStripMenuItem_Click(object sender, EventArgs e)
		{
			// if 0 accounts, this will not be visible
			// if 1 account, run scanLibrariesRemovedBooks() on this account
			// if multiple accounts, another menu set will open. do not run scanLibrariesRemovedBooks()
			using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
			var accounts = persister.AccountsSettings.GetAll();

			if (accounts.Count != 1)
				return;

			var firstAccount = accounts.Single();
			scanLibrariesRemovedBooks(firstAccount);
		}

		// selectively remove books from all accounts
		private void removeAllAccountsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
			var allAccounts = persister.AccountsSettings.GetAll();
			scanLibrariesRemovedBooks(allAccounts.ToArray());
		}

		// selectively remove books from some accounts
		private void removeSomeAccountsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using var scanAccountsDialog = new ScanAccountsDialog(this);

			if (scanAccountsDialog.ShowDialog() != DialogResult.OK)
				return;

			if (!scanAccountsDialog.CheckedAccounts.Any())
				return;

			scanLibrariesRemovedBooks(scanAccountsDialog.CheckedAccounts.ToArray());
		}

		private void scanLibrariesRemovedBooks(params Account[] accounts)
		{
			using var dialog = new RemoveBooksDialog(accounts);
			dialog.ShowDialog();

			if (dialog.BooksRemoved)
				reloadGridAndUpdateBottomNumbers();
		}

		private void scanLibraries(IEnumerable<Account> accounts) => scanLibraries(accounts.ToArray());
		private void scanLibraries(params Account[] accounts)
		{
			using var dialog = new IndexLibraryDialog(accounts);
			dialog.ShowDialog();

			var totalProcessed = dialog.TotalBooksProcessed;
			var newAdded = dialog.NewBooksAdded;

			MessageBox.Show($"Total processed: {totalProcessed}\r\nNew: {newAdded}");

			if (totalProcessed > 0)
				reloadGridAndUpdateBottomNumbers();
		}
		#endregion

		#region liberate menu
		private async void beginBookBackupsToolStripMenuItem_Click(object sender, EventArgs e)
			=> await BookLiberation.ProcessorAutomationController.BackupAllBooksAsync();

		private async void beginPdfBackupsToolStripMenuItem_Click(object sender, EventArgs e)
			=> await BookLiberation.ProcessorAutomationController.BackupAllPdfsAsync();

		private async void convertAllM4bToMp3ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var result = MessageBox.Show(
				"This converts all m4b titles in your library to mp3 files. Original files are not deleted."
				+ "\r\nFor large libraries this will take a long time and will take up more disk space."
				+ "\r\n\r\nContinue?"
				+ "\r\n\r\n(To always download titles as mp3 instead of m4b, go to Settings: Download my books as .MP3 files)",
				"Convert all M4b => Mp3?",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Warning);
			if (result == DialogResult.Yes)
				await BookLiberation.ProcessorAutomationController.ConvertAllBooksAsync();
		}

		#endregion

		#region Export menu
		private void exportLibraryToolStripMenuItem_Click(object sender, EventArgs e)
		{
			try
			{
				var saveFileDialog = new SaveFileDialog
				{
					Title = "Where to export Library",
					Filter = "Excel Workbook (*.xlsx)|*.xlsx|CSV files (*.csv)|*.csv|JSON files (*.json)|*.json" // + "|All files (*.*)|*.*"
				};

				if (saveFileDialog.ShowDialog() != DialogResult.OK)
					return;

				// FilterIndex is 1-based, NOT 0-based
				switch (saveFileDialog.FilterIndex)
				{
					case 1: // xlsx
					default:
						LibraryExporter.ToXlsx(saveFileDialog.FileName);
						break;
					case 2: // csv
						LibraryExporter.ToCsv(saveFileDialog.FileName);
						break;
					case 3: // json
						LibraryExporter.ToJson(saveFileDialog.FileName);
						break;
				}

				MessageBox.Show("Library exported to:\r\n" + saveFileDialog.FileName);
			}
			catch (Exception ex)
			{
				MessageBoxAlertAdmin.Show("Error attempting to export your library.", "Error exporting", ex);
			}
		}
		#endregion

		#region quick filters menu
		private void loadInitialQuickFilterState()
		{
			// set inital state. do once only
			firstFilterIsDefaultToolStripMenuItem.Checked = QuickFilters.UseDefault;

			// load default filter. do once only
			if (QuickFilters.UseDefault)
				doFilter(QuickFilters.Filters.FirstOrDefault());

			// do after every save
			UpdateFilterDropDown();
		}

		private void FirstFilterIsDefaultToolStripMenuItem_Click(object sender, EventArgs e)
		{
			firstFilterIsDefaultToolStripMenuItem.Checked = !firstFilterIsDefaultToolStripMenuItem.Checked;
			QuickFilters.UseDefault = firstFilterIsDefaultToolStripMenuItem.Checked;
		}

		private object quickFilterTag { get; } = new object();
		public void UpdateFilterDropDown()
		{
			// remove old
			for (var i = quickFiltersToolStripMenuItem.DropDownItems.Count - 1; i >= 0; i--)
			{
				var menuItem = quickFiltersToolStripMenuItem.DropDownItems[i];
				if (menuItem.Tag == quickFilterTag)
					quickFiltersToolStripMenuItem.DropDownItems.Remove(menuItem);
			}

			// re-populate
			var index = 0;
			foreach (var filter in QuickFilters.Filters)
			{
				var menuItem = new ToolStripMenuItem
				{
					Tag = quickFilterTag,
					Text = $"&{++index}: {filter}"
				};
				menuItem.Click += (_, __) => doFilter(filter);
				quickFiltersToolStripMenuItem.DropDownItems.Add(menuItem);
			}
		}

		private void EditQuickFiltersToolStripMenuItem_Click(object sender, EventArgs e) => new EditQuickFilters(this).ShowDialog();
		#endregion

		#region settings menu
		private void accountsToolStripMenuItem_Click(object sender, EventArgs e) => new AccountsDialog(this).ShowDialog();

		private void basicSettingsToolStripMenuItem_Click(object sender, EventArgs e) => new SettingsDialog().ShowDialog();
		#endregion
	}
}
