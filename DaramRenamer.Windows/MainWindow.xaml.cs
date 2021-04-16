﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Daramee.Winston.Dialogs;
using DaramRenamer.Commands;

namespace DaramRenamer
{
	public partial class MainWindow : Window, INotifyPropertyChanged
	{
		private readonly UndoManager<ObservableCollection<FileInfo>> undoManager =
			new UndoManager<ObservableCollection<FileInfo>>();

		private BatchWindow currentBatchWindow = null;

		public bool UndoManagerHasUndoStackItem => !undoManager.IsUndoStackEmpty;
		public bool UndoManagerHasRedoStackItem => !undoManager.IsRedoStackEmpty;

		private PreferencesWindow preferencesWindow;

		#region Commands

		public static RoutedCommand CommandOpenFiles = new RoutedCommand();
		public static RoutedCommand CommandClearList = new RoutedCommand();
		public static RoutedCommand CommandApplyFile = new RoutedCommand();
		public static RoutedCommand CommandUndoWorks = new RoutedCommand();
		public static RoutedCommand CommandRedoWorks = new RoutedCommand();
		public static RoutedCommand CommandUpperItem = new RoutedCommand();
		public static RoutedCommand CommandLowerItem = new RoutedCommand();
		public static RoutedCommand CommandItemsSort = new RoutedCommand();

		public static RoutedCommand CommandCustom0 = new RoutedCommand();
		public static RoutedCommand CommandCustom1 = new RoutedCommand();
		public static RoutedCommand CommandCustom2 = new RoutedCommand();
		public static RoutedCommand CommandCustom3 = new RoutedCommand();
		public static RoutedCommand CommandCustom4 = new RoutedCommand();
		public static RoutedCommand CommandCustom5 = new RoutedCommand();
		public static RoutedCommand CommandCustom6 = new RoutedCommand();
		public static RoutedCommand CommandCustom7 = new RoutedCommand();
		public static RoutedCommand CommandCustom8 = new RoutedCommand();
		public static RoutedCommand CommandCustom9 = new RoutedCommand();

		private void CommandOpenFiles_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			MenuFileOpen_Click(sender, e);
		}

		private void CommandClearList_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			MenuFileClear_Click(sender, e);
		}

		private void CommandApplyFile_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			MenuFileApply_Click(sender, e);
		}

		private void CommandUndoWorks_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			MenuEditUndo_Click(sender, e);
		}

		private void CommandRedoWorks_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			MenuEditRedo_Click(sender, e);
		}

		private void CommandApplyCanc_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			while (!undoManager.IsUndoStackEmpty) MenuEditUndo_Click(sender, e);
		}

		private void CommandUpperItem_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			MenuEditItemToUp_Click(sender, e);
		}

		private void CommandLowerItem_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			MenuEditItemToDown_Click(sender, e);
		}

		private void CommandItemsSort_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			MenuEditSort_Click(sender, e);
		}

		private void CommandCustom_Executed (object sender, ExecutedRoutedEventArgs e)
		{
			string command = null;
			if (sender == commandBindingCustom0)
				command = Preferences.Instance.Shortcut0.Command;
			else if (sender == commandBindingCustom1)
				command = Preferences.Instance.Shortcut1.Command;
			else if (sender == commandBindingCustom2)
				command = Preferences.Instance.Shortcut2.Command;
			else if (sender == commandBindingCustom3)
				command = Preferences.Instance.Shortcut3.Command;
			else if (sender == commandBindingCustom4)
				command = Preferences.Instance.Shortcut4.Command;
			else if (sender == commandBindingCustom5)
				command = Preferences.Instance.Shortcut5.Command;
			else if (sender == commandBindingCustom6)
				command = Preferences.Instance.Shortcut6.Command;
			else if (sender == commandBindingCustom7)
				command = Preferences.Instance.Shortcut7.Command;
			else if (sender == commandBindingCustom8)
				command = Preferences.Instance.Shortcut8.Command;
			else if (sender == commandBindingCustom9)
				command = Preferences.Instance.Shortcut9.Command;

			Type commandType = null;
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				foreach (var type in assembly.GetTypes())
				{
					if (type.FullName == command)
					{
						commandType = type;
						break;
					}
				}

				if (commandType != null)
					break;
			}

			var commandObj = Activator.CreateInstance(commandType);
			if (commandObj == null)
				return;

			if (commandObj is ICommand)
				CommandMenuItem_Click(commandObj, null);
		}

		#endregion

		private static async Task<bool> IsAdministrator()
		{
			return await Task.Run(() => new WindowsPrincipal(WindowsIdentity.GetCurrent())
				.IsInRole(WindowsBuiltInRole.Administrator));
		}

		public event PropertyChangedEventHandler PropertyChanged;

		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		public MainWindow()
		{
			InitializeComponent();
			FileInfo.FileOperator = new WindowsNativeFileOperator();

			Title = $"{Strings.Instance["DaramRenamer"]} - {Strings.Instance["Version"]} {GetVersionString()}";

			if (Preferences.Instance.UseCustomPlugins)
				PluginManager.Instance.LoadPlugins();

			PluginToMenu.InitializeCommands(CommandsMenu.Items);
			PluginToMenu.InitializeCommands(ListViewContextMenu.Items);

			PluginToMenu.InitializeConditions(ConditionsMenu);

			undoManager.UpdateUndo += (sender, e) =>
			{
				OnPropertyChanged(nameof(UndoManagerHasUndoStackItem));
				OnPropertyChanged(nameof(UndoManagerHasRedoStackItem));
			};
			undoManager.UpdateRedo += (sender, e) =>
			{
				OnPropertyChanged(nameof(UndoManagerHasUndoStackItem));
				OnPropertyChanged(nameof(UndoManagerHasRedoStackItem));
			};

			ListViewFiles.ItemsSource = FileInfo.Files;

			if (Preferences.Instance.SaveWindowState)
			{
				Left = Preferences.Instance.Left;
				Top = Preferences.Instance.Top;
				Width = Preferences.Instance.Width;
				Height = Preferences.Instance.Height;
				WindowState = Preferences.Instance.WindowState;
			}

			Preferences.Instance.ShortcutRebinding();
		}

		private async void Window_Loaded(object sender, RoutedEventArgs e)
		{
			if (!Environment.Is64BitProcess)
				Title += " - [32-Bit]";
			if (await IsAdministrator())
				Title = $"{Title} - [{Strings.Instance["Administrator"]}]";

			var updateInfo = await CheckUpdate();

			Title = updateInfo switch
			{
				true => $"{Title} - [{Strings.Instance["NewLatestVersionAvailable"]}]",
				null => $"{Title} - [{Strings.Instance["UpdateCheckError"]}]",
				_ => Title
			};
		}

		public async void RefreshTitle()
		{
			Title = $"{Strings.Instance["DaramRenamer"]} - {Strings.Instance["Version"]} {GetVersionString()}";

			if (!Environment.Is64BitProcess)
				Title += " - [32-Bit]";
			if (await IsAdministrator())
				Title = $"{Title} - [{Strings.Instance["Administrator"]}]";

			var updateInfo = await CheckUpdate();

			Title = updateInfo switch
			{
				true => $"{Title} - [{Strings.Instance["NewLatestVersionAvailable"]}]",
				null => $"{Title} - [{Strings.Instance["UpdateCheckError"]}]",
				_ => Title
			};
		}

		internal static async Task<bool?> CheckUpdate()
		{
			return await Task.Run<bool?>(() =>
			{
				var updateInformation = UpdateInformationBank.GetUpdateInformation(TargetPlatform.Windows);
				if (updateInformation != null)
					return updateInformation.Value.StableLatestVersion != GetVersionString();
				return null;
			});
		}

		private void Window_Closing(object sender, CancelEventArgs e)
		{
			if (Preferences.Instance.SaveWindowState)
			{
				Preferences.Instance.Left = Left;
				Preferences.Instance.Top = Top;
				Preferences.Instance.Width = Width;
				Preferences.Instance.Height = Height;
				Preferences.Instance.WindowState = WindowState;
			}
			Preferences.Instance.Save();
		}

		private void Window_Unloaded(object sender, RoutedEventArgs e)
		{
			preferencesWindow.Close();
			preferencesWindow = null;
		}

		internal static string GetVersionString()
		{
			var programVersion = Assembly.GetExecutingAssembly().GetName().Version;
			return programVersion != null
				? $"{programVersion.Major}.{programVersion.Minor}.{programVersion.Build}"
				: "UNKNOWN VERSION";
		}

		public static TaskDialogResult MessageBox(string message, string content, TaskDialogIcon icon,
			TaskDialogCommonButtonFlags commonButtons, params string[] buttons)
		{
			var tdButtons = buttons != null ? TaskDialogButton.Cast(buttons) : null;

			var taskDialog = new TaskDialog
			{
				Title = Strings.Instance["DaramRenamer"],
				MainInstruction = message,
				Content = content,
				MainIcon = icon,
				CommonButtons = commonButtons,
				Buttons = tdButtons,
			};
			return taskDialog.Show(Application.Current.MainWindow);
		}

		public void AddItem(string s, bool directoryMode = false)
		{
			if (FileInfo.FileOperator.FileExists(s) || directoryMode)
			{
				var fileInfo = new FileInfo(s);
				if (!FileInfo.Files.Contains(fileInfo))
					FileInfo.Files.Add(fileInfo);
			}
			else
			{
				foreach (var ss in FileInfo.FileOperator.GetFiles(s, false))
					AddItem(ss);
			}
		}

		private void ListViewFiles_DragEnter(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
				e.Effects = DragDropEffects.None;
		}

		private void ListViewFiles_Drop(object sender, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(DataFormats.FileDrop))
				return;

			if (!(e.Data.GetData(DataFormats.FileDrop) is string[] temp))
				return;

			var hasDirectory = false;
			foreach (var filename in temp)
			{
				if (File.GetAttributes(filename).HasFlag(FileAttributes.Directory) && filename.Length > 3)
					hasDirectory = true;
			}

			var directoryMode = false;
			if (hasDirectory)
			{
				var result = MessageBox(Strings.Instance["DragAndDrop_DirectoryQuestion"],
					Strings.Instance["DragAndDrop_DirectoryQuestionDescription"],
					TaskDialogIcon.Warning, TaskDialogCommonButtonFlags.Cancel,
					Strings.Instance["DragAndDrop_ButtonAddDirectory"],
					Strings.Instance["DragAndDrop_ButtonIterateFiles"]);
				if (result.Button == TaskDialogResult.Cancel)
					return;
				directoryMode = result.Button == 101;
			}

			undoManager.SaveToUndoStack(FileInfo.Files);

			foreach (var s in from b in temp orderby b select b)
				AddItem(s,
					(s.Length > 3 && File.GetAttributes(s).HasFlag(FileAttributes.Directory)) && directoryMode);
		}

		private void ListViewFiles_KeyUp(object sender, KeyEventArgs e)
		{
			if (e.Key != Key.Delete)
				return;

			undoManager.SaveToUndoStack(FileInfo.Files);

			foreach (var fileInfo in ListViewFiles.SelectedItems.Cast<FileInfo>().ToList())
				FileInfo.Files.Remove(fileInfo);
		}

		private void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
		{
			if ((sender as ListViewItem)?.Content is not FileInfo info)
				return;

			var command = new ManualEditCommand
			{
				ChangeName = info.ChangedFilename,
				ChangePath = info.ChangedPath
			};
			
			var commandWindow = new CommandWindow(command) { Owner = this };
			
			if (commandWindow.ShowDialog() != true)
				return;

			DoApplyCommand(command, new DesignatedFilesRoutedEventArgs(info));
		}

		private void MenuFileOpen_Click(object sender, RoutedEventArgs e)
		{
			Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
			{
				Title = Strings.Instance["FileDialogTitleOpenFiles"],
				Filter = Strings.Instance["FileDialogFilterAllFiles"],
				Multiselect = true
			};
			if (openFileDialog.ShowDialog() == false) return;

			undoManager.SaveToUndoStack(FileInfo.Files);

			foreach (var s in from s in openFileDialog.FileNames orderby s select s)
				AddItem(s);
		}

		private void MenuFileFolderOpen_Click(object sender, RoutedEventArgs e)
		{
			OpenFolderDialog openFolderDialog = new OpenFolderDialog
			{
				Title = Strings.Instance["FileDialogTitleOpenFiles"],
				AllowMultiSelection = true
			};
			if (openFolderDialog.ShowDialog() == false) return;

			undoManager.SaveToUndoStack(FileInfo.Files);

			foreach (var s in from s in openFolderDialog.FileNames orderby s select s)
				AddItem(s, true);
		}

		private void MenuFileClear_Click(object sender, RoutedEventArgs e)
		{
			undoManager.ClearAll();
			FileInfo.Files.Clear();
		}

		private void MenuFileApply_Click(object sender, RoutedEventArgs e)
		{
			new ApplyWindow(undoManager) {Owner = this}.ShowDialog();
		}

		private void MenuFileExit_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		private void MenuEditUndo_Click(object sender, RoutedEventArgs e)
		{
			if (undoManager.IsUndoStackEmpty)
				return;

			undoManager.SaveToRedoStack(FileInfo.Files);

			var data = undoManager.LoadFromUndoStack() ?? throw new Exception();
			ListViewFiles.ItemsSource = FileInfo.Files = data;
		}

		private void MenuEditRedo_Click(object sender, RoutedEventArgs e)
		{
			if (undoManager.IsRedoStackEmpty)
				return;

			undoManager.SaveToUndoStack(FileInfo.Files, false);

			var data = undoManager.LoadFromRedoStack() ?? throw new Exception();
			ListViewFiles.ItemsSource = FileInfo.Files = data;
		}

		private void MenuEditItemToUp_Click(object sender, RoutedEventArgs e)
		{
			if (ListViewFiles.SelectedItems.Count == 0) return;
			undoManager.SaveToUndoStack(FileInfo.Files);
			foreach (FileInfo fileInfo in ListViewFiles.SelectedItems)
			{
				var lastIndex = FileInfo.Files.IndexOf(fileInfo);
				if (lastIndex == 0) continue;
				FileInfo.Files.Move(lastIndex, lastIndex - 1);
			}
		}

		private void MenuEditItemToDown_Click(object sender, RoutedEventArgs e)
		{
			if (ListViewFiles.SelectedItems.Count == 0) return;
			undoManager.SaveToUndoStack(FileInfo.Files);
			foreach (FileInfo fileInfo in ListViewFiles.SelectedItems)
			{
				var lastIndex = FileInfo.Files.IndexOf(fileInfo);
				if (lastIndex == FileInfo.Files.Count - 1) continue;
				FileInfo.Files.Move(lastIndex, lastIndex + 1);
			}
		}

		private void MenuEditSort_Click(object sender, RoutedEventArgs e)
		{
			undoManager.SaveToUndoStack(FileInfo.Files);
			FileInfo.Sort(FileInfo.Files);
		}

		private IEnumerable<ICondition> GetActivedConditions()
		{
			foreach (ICondition condition in ConditionsMenu.ItemsSource)
			{
				var item = ConditionsMenu.ItemContainerGenerator.ContainerFromItem(condition);
				if (item == null || (item is MenuItem menuItem && menuItem.IsChecked != true))
					continue;
				yield return condition;
			}
		}

		private void CommandMenuItem_Click(object sender, RoutedEventArgs e)
		{
			ICommand command;
			if ((sender as MenuItem)?.Header is ICommand asCommandFromMenuItem)
				command = asCommandFromMenuItem;
			else if (sender is Button button)
				command = PluginManager.Instance.FindCommand(button.Tag as string);
			else if (sender is ICommand asCommand)
				command = asCommand;
			else
				return;

			if (command == null)
				return;

			command = Activator.CreateInstance(command.GetType()) as ICommand;

			var currentChanges = undoManager.SaveTemporary(FileInfo.Files);

			var properties = command?.GetType().GetProperties();
			if (properties?.Length > (command is IOrderBy ? 3 : 2))
			{
				var commandWindow = new CommandWindow(command) {Owner = this};
				commandWindow.ValueChanged += (sender1, e1) =>
				{
					if (Preferences.Instance.VisualCommand)
					{
						FileInfo.Files = undoManager.LoadTemporary(currentChanges);
						DoApplyCommand(command, e);
						ListViewFiles.ItemsSource = FileInfo.Files;
					}
				};

				try
				{
					if (commandWindow.ShowDialog() != true)
						return;
				}
				finally
				{
					ListViewFiles.ItemsSource = FileInfo.Files = undoManager.LoadTemporary(currentChanges);
				}
			}

			undoManager.SaveToUndoStack(FileInfo.Files);

			DoApplyCommand(command, e);

			ListViewFiles.ItemsSource = FileInfo.Files;
		}

		private void DoApplyCommand(ICommand command, RoutedEventArgs e)
		{
			var conditions = GetActivedConditions().ToArray();

			{
				var targets =
					e is DesignatedFilesRoutedEventArgs designatedFilesRoutedEventArgs
						? designatedFilesRoutedEventArgs.DesignatedFiles
						: FileInfo.Files as IEnumerable<FileInfo>;

				if (command is ITargetContains targetContains)
					targetContains.SetTargets(targets);

				if (command.ParallelProcessable && !Preferences.Instance.ForceSingleCoreRunning)
					Parallel.ForEach(targets, file =>
					{
						if (conditions.All(condition => condition.IsSatisfyThisCondition(file)))
							command.DoCommand(file);
					});
				else
					foreach (var file in targets)
						if (conditions.All(condition => condition.IsSatisfyThisCondition(file)))
							command.DoCommand(file);
			}
		}

		private void ConditionMenuItem_Click(object sender, RoutedEventArgs e)
		{
			if (((MenuItem) sender).IsChecked)
			{
				var condition = ((MenuItem) sender).Header as ICondition;
				var properties = condition.GetType().GetProperties();
				if (properties.Length > (condition is IOrderBy ? 1 : 0))
				{
					var commandWindow = new CommandWindow(condition) {Owner = this};
					if (commandWindow.ShowDialog() != true)
						((MenuItem) sender).IsChecked = false;
				}
			}
		}

		private readonly ManualEditCommand ManualEditCommand = new ManualEditCommand();

		private class DesignatedFilesRoutedEventArgs : RoutedEventArgs
		{
			public FileInfo[] DesignatedFiles { get; }

			public DesignatedFilesRoutedEventArgs(params FileInfo[] files)
			{
				DesignatedFiles = files;
			}
		}

		private void MenuItemPreferences_Click(object sender, RoutedEventArgs e)
		{
			preferencesWindow ??= new PreferencesWindow();
			if (preferencesWindow.IsActive)
				return;

			preferencesWindow.Owner = this;
			preferencesWindow.Show();
		}

		private async void MenuToolsCheckUpdate_Click(object sender, RoutedEventArgs e)
		{
			var update = await CheckUpdate();
			if (update == true)
			{
				var result = MessageBox(Strings.Instance["NewLatestVersionAvailableText"], "",
					TaskDialogIcon.Information,
					TaskDialogCommonButtonFlags.OK, Strings.Instance["ButtonUpdate"]);

				if (result.Button == 101)
				{
					var updateInfo = UpdateInformationBank.GetUpdateInformation(TargetPlatform.Windows);
					if (updateInfo != null)
					{
						try
						{
							var psInfo = new ProcessStartInfo("cmd")
							{
								Arguments =
									$"/C start DaramRenamer.UpdateAgent.exe {updateInfo.Value.StableLatestVersion} {updateInfo.Value.StableLatestUrl}",
								UseShellExecute = true,
							};
							Process.Start(psInfo);
							Application.Current.Shutdown();
						}
						catch
						{
							var psInfo = new ProcessStartInfo("https://github.com/daramkun/DaramRenamer/releases")
							{
								UseShellExecute = true,
							};
							Process.Start(psInfo);
						}
					}
					else
					{

					}
				}
			}
			else
			{
				MessageBox(Strings.Instance[update == false ? "ThisIsLatestVersionText" : "UpdateCheckErrorText"], "",
					update == null ? TaskDialogIcon.Error : null, TaskDialogCommonButtonFlags.OK);
			}
		}

		private void MenuHelpLicenses_Click(object sender, RoutedEventArgs e)
		{
			new LicenseWindow() {Owner = this}.ShowDialog();
		}

		private void MenuHelpAbout_Click(object sender, RoutedEventArgs e)
		{
			new AboutWindow() {Owner = this}.ShowDialog();
		}

		private void MenuItemBatch_Click(object sender, RoutedEventArgs e)
		{
			if (currentBatchWindow == null || (currentBatchWindow != null && !currentBatchWindow.IsVisible))
				currentBatchWindow = new BatchWindow(this);

			currentBatchWindow.Show();
		}
	}
}
