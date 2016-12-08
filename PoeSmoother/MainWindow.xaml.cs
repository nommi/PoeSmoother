namespace PoeSmoother
{
    using Ionic.Zip;
    using LibGGPK;
    using Microsoft.Win32;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private string ggpkPath = string.Empty;
        private GGPK content;
        private Thread workerThread;

        /// <summary>
        /// Dictionary mapping ggpk file paths to FileRecords for easy lookup
        /// EG: "Scripts\foobar.mel" -> FileRecord{Foobar.mel}
        /// </summary>
        private Dictionary<string, FileRecord> RecordsByPath;

        public MainWindow()
        {
            InitializeComponent();
            MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
        }

        private void OutputLine(string msg)
        {
            Output(msg + Environment.NewLine);
        }
        private void Output(string msg)
        {
            textBoxOutput.Dispatcher.BeginInvoke(new Action(() =>
            {
                textBoxOutput.Text += msg;
            }), null);
        }
        private void UpdateTitle(string newTitle)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Title = newTitle;
            }), null);
        }

        /// <summary>
        /// Reloads the entire content.ggpk, rebuilds the tree
        /// </summary>
        private void ReloadGGPK()
        {
            treeView.Items.Clear();
            ResetViewer();
            textBoxOutput.Visibility = Visibility.Visible;
            textBoxOutput.Text = string.Empty;
            content = null;

            workerThread = new Thread(() =>
            {
                content = new GGPK();
                try
                {
                    content.Read(ggpkPath, Output);
                }
                catch (Exception ex)
                {
                    Output(string.Format(Settings.Strings["ReloadGGPK_Failed"], ex.Message));
                    return;
                }
                if (content.IsReadOnly)
                {
                    Output(Settings.Strings["ReloadGGPK_ReadOnly"] + Environment.NewLine);
                    UpdateTitle(Settings.Strings["MainWindow_Title_Readonly"]);
                }
                OutputLine(Settings.Strings["ReloadGGPK_Traversing_Tree"]);

                // Collect all FileRecordPath -> FileRecord pairs for easier replacing
                RecordsByPath = new Dictionary<string, FileRecord>(content.RecordOffsets.Count);
                DirectoryTreeNode.TraverseTreePostorder(content.DirectoryRoot, null, n => RecordsByPath.Add(n.GetDirectoryPath() + n.Name, n));
                treeView.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        AddDirectoryTreeToControl(content.DirectoryRoot, null);
                    }
                    catch (Exception ex)
                    {
                        Output(string.Format(Settings.Strings["Error_Read_Directory_Tree"], ex.Message));
                        return;
                    }
                    workerThread = null;
                }), null);
                OutputLine(Settings.Strings["ReloadGGPK_Successful"]);
            });
            workerThread.Start();
        }

        /// <summary>
        /// Recursivly adds the specified GGPK DirectoryTree to the TreeListView
        /// </summary>
        /// <param name="directoryTreeNode">Node to add to tree</param>
        /// <param name="parentControl">TreeViewItem to add children to</param>
        private void AddDirectoryTreeToControl(DirectoryTreeNode directoryTreeNode, ItemsControl parentControl)
        {
            TreeViewItem rootItem = new TreeViewItem { Header = directoryTreeNode };
            if ((directoryTreeNode.ToString() == "ROOT") || (directoryTreeNode.ToString() == "")) rootItem.IsExpanded = true;

            if (parentControl == null)
            {
                treeView.Items.Add(rootItem);
            }
            else
            {
                parentControl.Items.Add(rootItem);
            }
            directoryTreeNode.Children.Sort();
            foreach (var item in directoryTreeNode.Children)
            {
                AddDirectoryTreeToControl(item, rootItem);
            }
            directoryTreeNode.Files.Sort();
            foreach (var item in directoryTreeNode.Files)
            {
                rootItem.Items.Add(item);
            }
        }

        /// <summary>
        /// Resets all of the file viewers
        /// </summary>
        private void ResetViewer()
        {
            textBoxOutput.Visibility = Visibility.Hidden;
            richTextOutput.Visibility = Visibility.Hidden;
            textBoxOutput.Clear();
            richTextOutput.Document.Blocks.Clear();
        }

        /// <summary>
        /// Updates the FileViewers to display the currently selected item in the TreeView
        /// </summary>
        private void UpdateDisplayPanel()
        {
            ResetViewer();
            if (treeView.SelectedItem == null)
            {
                return;
            }
            var item = treeView.SelectedItem as TreeViewItem;
            if (item?.Header is DirectoryTreeNode)
            {
                DirectoryTreeNode selectedDirectory = (DirectoryTreeNode)item.Header;
                if (selectedDirectory.Record == null) return;
            }
            FileRecord selectedRecord = treeView.SelectedItem as FileRecord;
            if (selectedRecord == null) return;
            try
            {
                switch (selectedRecord.FileFormat)
                {
                    case FileRecord.DataFormat.Ascii: DisplayAscii(selectedRecord); break;
                    case FileRecord.DataFormat.Unicode: DisplayUnicode(selectedRecord); break;
                    case FileRecord.DataFormat.RichText: DisplayRichText(selectedRecord); break;
                }
            }
            catch (Exception ex)
            {
                ResetViewer();
                textBoxOutput.Visibility = Visibility.Visible;
                StringBuilder sb = new StringBuilder();
                while (ex != null)
                {
                    sb.AppendLine(ex.Message);
                    ex = ex.InnerException;
                }
                textBoxOutput.Text = string.Format(Settings.Strings["UpdateDisplayPanel_Failed"], sb);
            }
        }

        /// <summary>
        /// Displays the contents of a FileRecord in the RichTextBox
        /// </summary>
        /// <param name="selectedRecord">FileRecord to display</param>
        private void DisplayRichText(FileRecord selectedRecord)
        {
            byte[] buffer = selectedRecord.ReadData(ggpkPath);
            richTextOutput.Visibility = Visibility.Visible;
            using (MemoryStream ms = new MemoryStream(buffer))
            {
                richTextOutput.Selection.Load(ms, DataFormats.Rtf);
            }
        }

        /// <summary>
        /// Displays the contents of a FileRecord in the TextBox as Unicode text
        /// </summary>
        /// <param name="selectedRecord">FileRecord to display</param>
        private void DisplayUnicode(FileRecord selectedRecord)
        {
            byte[] buffer = selectedRecord.ReadData(ggpkPath);
            textBoxOutput.Visibility = Visibility.Visible;
            textBoxOutput.Text = Encoding.Unicode.GetString(buffer);
        }

        /// <summary>
        /// Displays the contents of a FileRecord in the TextBox as Ascii text
        /// </summary>
        /// <param name="selectedRecord">FileRecord to display</param>
        private void DisplayAscii(FileRecord selectedRecord)
        {
            byte[] buffer = selectedRecord.ReadData(ggpkPath);
            textBoxOutput.Visibility = Visibility.Visible;
            textBoxOutput.Text = Encoding.ASCII.GetString(buffer);
        }

        /// <summary>
        /// Exports the specified FileRecord to disk
        /// </summary>
        /// <param name="selectedRecord">FileRecord to export</param>
        private void ExportFileRecord(FileRecord selectedRecord)
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog { FileName = selectedRecord.Name };
                if (saveFileDialog.ShowDialog() != true) return;
                selectedRecord.ExtractFile(ggpkPath, saveFileDialog.FileName);
                MessageBox.Show(string.Format(Settings.Strings["ExportSelectedItem_Successful"], 
                    selectedRecord.DataLength), Settings.Strings["ExportAllItemsInDirectory_Successful_Caption"], 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ExportSelectedItem_Failed"], ex.Message), 
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Exports entire DirectoryTreeNode to disk, preserving directory structure
        /// </summary>
        /// <param name="selectedDirectoryNode">Node to export to disk</param>
        private void ExportAllItemsInDirectory(DirectoryTreeNode selectedDirectoryNode)
        {
            List<FileRecord> recordsToExport = new List<FileRecord>();
            Action<FileRecord> fileAction = recordsToExport.Add;
            DirectoryTreeNode.TraverseTreePreorder(selectedDirectoryNode, null, fileAction);
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    FileName = Settings.Strings["ExportAllItemsInDirectory_Default_FileName"]
                };
                if (saveFileDialog.ShowDialog() != true) return;
                string exportDirectory = Path.GetDirectoryName(saveFileDialog.FileName) + Path.DirectorySeparatorChar;
                foreach (var item in recordsToExport)
                {
                    item.ExtractFileWithDirectoryStructure(ggpkPath, exportDirectory);
                }
                MessageBox.Show(string.Format(Settings.Strings["ExportAllItemsInDirectory_Successful"], recordsToExport.Count), 
                    Settings.Strings["ExportAllItemsInDirectory_Successful_Caption"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ExportAllItemsInDirectory_Failed"], ex.Message), 
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Replaces selected file with file user selects via MessageBox
        /// </summary>
        /// <param name="recordToReplace"></param>
        private void ReplaceItem(FileRecord recordToReplace)
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    FileName = "",
                    CheckFileExists = true,
                    CheckPathExists = true
                };
                if (openFileDialog.ShowDialog() != true) return;
                recordToReplace.ReplaceContents(ggpkPath, openFileDialog.FileName, content.FreeRoot);
                MessageBox.Show(string.Format(
                    Settings.Strings["ReplaceItem_Successful"], recordToReplace.Name, recordToReplace.RecordBegin.ToString("X")),
                    Settings.Strings["ReplaceItem_Successful_Caption"],
                    MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateDisplayPanel();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message), 
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Extracts specified archive and replaces files in GGPK with extracted files. Files in
        /// archive must have same directory structure as in GGPK.
        /// </summary>
        /// <param name="archivePath">Path to archive containing</param>
        private void HandleDropArchive(string archivePath)
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropArchive_Info"], archivePath));

            using (ZipFile zipFile = new ZipFile(archivePath))
            {
                //var fileNames = zipFile.EntryFileNames;

                // Archive Version Check: Read version.txt and check with patch_notes.rtf's Hash
                foreach (var item in zipFile.Entries.Where(item => item.FileName.Equals("version.txt")))
                {
                    using (var reader = item.OpenReader())
                    {
                        byte[] versionData = new byte[item.UncompressedSize];
                        reader.Read(versionData, 0, versionData.Length);
                        string versionStr = Encoding.UTF8.GetString(versionData, 0, versionData.Length);
                        if (RecordsByPath.ContainsKey("patch_notes.rtf"))
                        {
                            string Hash = BitConverter.ToString(RecordsByPath["patch_notes.rtf"].Hash);
                            if (!versionStr.Substring(0, Hash.Length).Equals(Hash))
                            {
                                OutputLine(Settings.Strings["MainWindow_VersionCheck_Failed"]); return;
                            }
                        }
                    } break;
                }

                foreach (var item in zipFile.Entries)
                {
                    if (item.IsDirectory) { continue; }
                    if (item.FileName.Equals("version.txt")) { continue; }
                    string fixedFileName = item.FileName;
                    if (Path.DirectorySeparatorChar != '/')
                    {
                        fixedFileName = fixedFileName.Replace('/', Path.DirectorySeparatorChar);
                    }
                    if (!RecordsByPath.ContainsKey(fixedFileName))
                    {
                        OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropDirectory_Failed"], fixedFileName)); continue;
                    }
                    OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropDirectory_Replace"], fixedFileName));
                    using (var reader = item.OpenReader())
                    {
                        byte[] replacementData = new byte[item.UncompressedSize];
                        reader.Read(replacementData, 0, replacementData.Length);
                        RecordsByPath[fixedFileName].ReplaceContents(ggpkPath, replacementData, content.FreeRoot);
                    }
                }
            }
        }

        /// <summary>
        /// Replaces the currently selected TreeViewItem with specified file on disk
        /// </summary>
        /// <param name="fileName">Path of file to replace currently selected item with.</param>
        private void HandleDropFile(string fileName)
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]); return;
            }
            FileRecord record = treeView.SelectedItem as FileRecord;
            if (record == null)
            {
                OutputLine(Settings.Strings["MainWindow_HandleDropFile_Failed"]); return;
            }
            OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropFile_Replace"], record.GetDirectoryPath(), record.Name));
            record.ReplaceContents(ggpkPath, fileName, content.FreeRoot);
        }

        /// <summary>
        /// Specified directory was dropped onto interface, attept to replace GGPK files with same directory
        /// structure with files in directory. Directory must have same directory structure as GGPK file.
        /// EG:
        /// dropping 'Art' directory containing '2DArt' directory containing 'BuffIcons' directory containing 'buffbleed.dds' will replace
        /// \Art\2DArt\BuffIcons\buffbleed.dds with buffbleed.dds from dropped directory
        /// </summary>
        /// <param name="baseDirectory">Directory containing files to replace</param>
        private void HandleDropDirectory(string baseDirectory)
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]); return;
            }
            string[] filesToReplace = Directory.GetFiles(baseDirectory, "*.*", SearchOption.AllDirectories);
            var fileName = Path.GetFileName(baseDirectory);
            {
                int baseDirectoryNameLength = fileName.Length;
                OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropDirectory_Count"], filesToReplace.Length));
                foreach (var item in filesToReplace)
                {
                    string fixedFileName = item.Remove(0, baseDirectory.Length - baseDirectoryNameLength);
                    if (!RecordsByPath.ContainsKey(fixedFileName))
                    {
                        OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropDirectory_Failed"], fixedFileName)); continue;
                    }
                    OutputLine(string.Format(Settings.Strings["MainWindow_HandleDropDirectory_Replace"], fixedFileName));
                    RecordsByPath[fixedFileName].ReplaceContents(ggpkPath, item, content.FreeRoot);
                }
            }
        }
        private void PoeSmoother_Loaded(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                CheckFileExists = true, Filter = Settings.Strings["Load_GGPK_Filter"]
            };
            // Get InstallLocation From RegistryKey
            if ((ofd.InitialDirectory == null) || (ofd.InitialDirectory == string.Empty))
            {
                RegistryKey start = Registry.CurrentUser;
                RegistryKey programName = start.OpenSubKey(@"Software\GrindingGearGames\Path of Exile");
                if (programName != null)
                {
                    string pathString = (string)programName.GetValue("InstallLocation");
                    if (pathString != string.Empty && File.Exists(pathString + @"\Content.ggpk"))
                    {
                        ofd.InitialDirectory = pathString;
                    }
                }
            }
            // Get Garena PoE
            if ((ofd.InitialDirectory == null) || (ofd.InitialDirectory == string.Empty))
            {
                RegistryKey start = Registry.LocalMachine;
                RegistryKey programName = start.OpenSubKey(@"SOFTWARE\Wow6432Node\Garena\PoE");
                if (programName != null)
                {
                    string pathString = (string)programName.GetValue("Path");
                    if (pathString != string.Empty && File.Exists(pathString + @"\Content.ggpk"))
                    {
                        ofd.InitialDirectory = pathString;
                    }
                }
            }
            if (ofd.ShowDialog() == true)
            {
                if (!File.Exists(ofd.FileName))
                {
                    Close();
                    return;
                }
                ggpkPath = ofd.FileName;
                ReloadGGPK();
            }
            else
            {
                Close();
                return;
            }
            menuItemExport.Header = Settings.Strings["MainWindow_Menu_Export"];
            menuItemReplace.Header = Settings.Strings["MainWindow_Menu_Replace"];
        }
        private void treeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            UpdateDisplayPanel();
            menuItemReplace.IsEnabled = treeView.SelectedItem is FileRecord;
            if (treeView.SelectedItem is FileRecord)
            {
                // Exporting file
                menuItemExport.IsEnabled = true;
            }
            else if ((treeView.SelectedItem as TreeViewItem)?.Header is DirectoryTreeNode)
            {
                // Exporting entire directory
                menuItemExport.IsEnabled = true;
            }
            else
            {
                menuItemExport.IsEnabled = false;
            }
        }
        private void menuItemExport_Click(object sender, RoutedEventArgs e)
        {
            var item = treeView.SelectedItem as TreeViewItem;
            if (item != null)
            {
                TreeViewItem selectedTreeViewItem = item;
                DirectoryTreeNode selectedDirectoryNode = selectedTreeViewItem.Header as DirectoryTreeNode;
                if (selectedDirectoryNode != null)
                {
                    ExportAllItemsInDirectory(selectedDirectoryNode);
                }
            }
            else if (treeView.SelectedItem is FileRecord)
            {
                ExportFileRecord((FileRecord)treeView.SelectedItem);
            }
        }
        private void menuItemReplace_Click(object sender, RoutedEventArgs e)
        {
            FileRecord recordToReplace = treeView.SelectedItem as FileRecord;
            if (recordToReplace == null) return;
            ReplaceItem(recordToReplace);
        }

        #region PoeSmoother
        private void Arc(object sender, RoutedEventArgs e) { Arc(); }
        private void ArcZero(object sender, RoutedEventArgs e) { ArcZero(); }
        private void ArcticBreath(object sender, RoutedEventArgs e) { ArcticBreath(); }
        private void ArcticBreathZero(object sender, RoutedEventArgs e) { ArcticBreathZero(); }
        private void BallLightning(object sender, RoutedEventArgs e) { BallLightning(); }
        private void BallLightningZero(object sender, RoutedEventArgs e) { BallLightningZero(); }
        private void BladeVortex(object sender, RoutedEventArgs e) { BladeVortex(); }
        private void BladeVortexZero(object sender, RoutedEventArgs e) { BladeVortexZero(); }
        private void Discharge(object sender, RoutedEventArgs e) { Discharge(); }
        private void DischargeZero(object sender, RoutedEventArgs e) { DischargeZero(); }
        private void HeraldOfIce(object sender, RoutedEventArgs e) { HeraldOfIce(); }
        private void HeraldOfIceZero(object sender, RoutedEventArgs e) { HeraldOfIceZero(); }
        private void LightningStrike(object sender, RoutedEventArgs e) { LightningStrike(); }
        private void LightningStrikeZero(object sender, RoutedEventArgs e) { LightningStrikeZero(); }
        private void OtherSkills(object sender, RoutedEventArgs e) { OtherSkills(); }
        private void OtherSkillsZero(object sender, RoutedEventArgs e) { OtherSkillsZero(); }
        private void Bladefall(object sender, RoutedEventArgs e) { Bladefall(); }
        private void BladefallZero(object sender, RoutedEventArgs e) { BladefallZero(); }
        private void StormCall(object sender, RoutedEventArgs e) { StormCall(); }
        private void StormCallZero(object sender, RoutedEventArgs e) { StormCallZero(); }
        private void WhisperingIce(object sender, RoutedEventArgs e) { WhisperingIce(); }
        private void WhisperingIceZero(object sender, RoutedEventArgs e) { WhisperingIceZero(); }

        private void Particles(object sender, RoutedEventArgs e) { Particles(); }
        private void Environments(object sender, RoutedEventArgs e) { Environments(); }
        private void SilentMobs(object sender, RoutedEventArgs e) { SilentMobs(); }
        private void SilentSkills(object sender, RoutedEventArgs e) { SilentSkills(); }
        private void UiChanges(object sender, RoutedEventArgs e) { UiChanges(); }
        private void Micro(object sender, RoutedEventArgs e) { Micro(); }
        private void OtherEffects(object sender, RoutedEventArgs e) { OtherEffects(); }
        private void DeadBodies(object sender, RoutedEventArgs e) { DeadBodies(); }
        private void Custom(object sender, RoutedEventArgs e) { Custom(); }
        private void SetShaders(object sender, RoutedEventArgs e) { SetShaders(); }
        private void PrivateEffects(object sender, RoutedEventArgs e) { PrivateEffects(); }
        private void ZeroEffects(object sender, RoutedEventArgs e) { ZeroEffects(); }
        private void ZeroParticles(object sender, RoutedEventArgs e) { ZeroParticles(); }
        private void BreachLeague(object sender, RoutedEventArgs e) { BreachLeague(); }

        //private void button_Click(object sender, RoutedEventArgs e)
        //{
        //    arc.IsChecked = false;
        //    arcZero.IsChecked = false;
        //    arcticBreath.IsChecked = false;
        //    arcticBreathZero.IsChecked = false;
        //    ballLightning.IsChecked = false;
        //    ballLightningZero.IsChecked = false;
        //    bladefall.IsChecked = false;
        //    bladefallZero.IsChecked = false;
        //    bladeVortex.IsChecked = false;
        //    bladeVortexZero.IsChecked = false;
        //    discharge.IsChecked = false;
        //    dischargeZero.IsChecked = false;
        //    heraldOfIce.IsChecked = false;
        //    heraldOfIceZero.IsChecked = false;
        //    lightningStrike.IsChecked = false;
        //    lightningStrikeZero.IsChecked = false;
        //    otherSkills.IsChecked = false;
        //    otherSkillsZero.IsChecked = false;
        //    stormCall.IsChecked = false;
        //    stormCallZero.IsChecked = false;
        //    whisperingIce.IsChecked = false;
        //    whisperingIceZero.IsChecked = false;

        //    particles.IsChecked = false;
        //    environments.IsChecked = false;
        //    silentMobs.IsChecked = false;
        //    silentSkills.IsChecked = false;
        //    uiChanges.IsChecked = false;
        //    micro.IsChecked = false;
        //    others.IsChecked = false;
        //    deadBodies.IsChecked = false;
        //    zeroEffects.IsChecked = false;
        //    zeroParticles.IsChecked = false;
        //    custom.IsChecked = false;
        //    shaders.IsChecked = false;
        //    skillEffects.IsChecked = false;
        //}

        #region skills
        private void Arc()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveEffects = "config/Skills/Arc/removeEffects/Metadata";
            const string RestoreDefault = "config/Skills/Arc/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (arc.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void ArcZero()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveEffects = "config/Skills/Arc/zeroEffects/Metadata";
            const string RestoreDefault = "config/Skills/Arc/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (arcZero.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void ArcticBreath()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveEffects = "config/Skills/Arctic Breath/removeEffects/Metadata";
            const string RestoreDefault = "config/Skills/Arctic Breath/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (arcticBreath.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void ArcticBreathZero()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveEffects = "config/Skills/Arctic Breath/zeroEffects/Metadata";
            const string RestoreDefault = "config/Skills/Arctic Breath/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (arcticBreathZero.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void BallLightning()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveEffects = "config/Skills/Ball Lightning/removeEffects/Metadata";
            const string RestoreDefault = "config/Skills/Ball Lightning/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (ballLightning.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void BallLightningZero()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveEffects = "config/Skills/Ball Lightning/zeroEffects/Metadata";
            const string RestoreDefault = "config/Skills/Ball Lightning/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (ballLightningZero.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void Bladefall()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Skills/Bladefall/removeEffects/Metadata";
            const string RestoreDefault = "config/Skills/Bladefall/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (bladefall.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void BladefallZero()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Skills/Bladefall/zeroEffects/Metadata";
            const string RestoreDefault = "config/Skills/Bladefall/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (bladefallZero.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void BladeVortex()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveEffects = "config/Skills/Blade Vortex/removeEffects/Metadata";
            const string RestoreDefault = "config/Skills/Blade Vortex/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (bladeVortex.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void BladeVortexZero()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveEffects = "config/Skills/Blade Vortex/zeroEffects/Metadata";
            const string RestoreDefault = "config/Skills/Blade Vortex/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (bladeVortexZero.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void Discharge()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveEffects = "config/Skills/Discharge/removeEffects/Metadata";
            const string RestoreDefault = "config/Skills/Discharge/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (discharge.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void DischargeZero()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveEffects = "config/Skills/Discharge/zeroEffects/Metadata";
            const string RestoreDefault = "config/Skills/Discharge/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (dischargeZero.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void HeraldOfIce()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveEffects = "config/Skills/Herald Of Ice/removeEffects/Metadata";
            const string RestoreDefault = "config/Skills/Herald Of Ice/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (heraldOfIce.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void HeraldOfIceZero()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }
            const string RemoveHeraldEffects = "config/Skills/Herald Of Ice/zeroEffects/Metadata";
            const string RestoreDefaultHerald = "config/Skills/Herald Of Ice/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveHeraldEffects) || !Directory.Exists(RestoreDefaultHerald)) return;
            string[] zero_HeraldEffects = Directory.GetFiles(RemoveHeraldEffects, "*.*", SearchOption.AllDirectories);
            var zero_HeraldEffects_path = Path.GetFileName(RemoveHeraldEffects);
            int zero_HeraldEffects_dir = zero_HeraldEffects_path.Length;

            string[] restore_defaultHerald = Directory.GetFiles(RestoreDefaultHerald, "*.*", SearchOption.AllDirectories);
            var restore_defaultHerald_path = Path.GetFileName(RestoreDefaultHerald);
            int restore_defaultHerald_dir = restore_defaultHerald_path.Length;

            try
            {
                switch (heraldOfIceZero.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in zero_HeraldEffects)
                        {
                            string fileNames = item.Remove(0, RemoveHeraldEffects.Length - zero_HeraldEffects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_defaultHerald)
                        {
                            string fileNames = item.Remove(0, RestoreDefaultHerald.Length - restore_defaultHerald_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void LightningStrike()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Skills/Lightning Strike/removeEffects/Metadata";
            const string RestoreDefault = "config/Skills/Lightning Strike/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (lightningStrike.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void LightningStrikeZero()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Skills/Lightning Strike/zeroEffects/Metadata";
            const string RestoreDefault = "config/Skills/Lightning Strike/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (lightningStrikeZero.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void StormCall()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Skills/Storm Call/removeEffects/Metadata";
            const string RestoreDefault = "config/Skills/Storm Call/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (stormCall.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void StormCallZero()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Skills/Storm Call/zeroEffects/Metadata";
            const string RestoreDefault = "config/Skills/Storm Call/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (stormCallZero.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void OtherSkills()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Skills/Other Skills/removeEffects/Metadata";
            const string RestoreDefault = "config/Skills/Other Skills/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (otherSkills.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void OtherSkillsZero()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Skills/Other Skills/zeroEffects/Metadata";
            const string RestoreDefault = "config/Skills/Other Skills/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (otherSkillsZero.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void WhisperingIce()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Skills/WhisperingIce/removeEffects/Metadata";
            const string RestoreDefault = "config/Skills/WhisperingIce/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (whisperingIce.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void WhisperingIceZero()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Skills/Whispering Ice/zeroEffects/Metadata";
            const string RestoreDefault = "config/Skills/Whispering Ice/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (whisperingIceZero.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        private void Particles()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Particles/removeEffects/Metadata";
            const string RestoreDefault = "config/Particles/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (particles.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Environments()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Environment/removeEffects/Metadata";
            const string RestoreDefault = "config/Environment/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (environments.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SilentMobs()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Sounds/SilentMonsters/removeEffects/Metadata";
            const string RestoreDefault = "config/Sounds/SilentMonsters/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (silentMobs.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SilentSkills()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Sounds/SilentSkills/removeEffects/Metadata";
            const string RestoreDefault = "config/Sounds/SilentSkills/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (silentSkills.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OtherEffects()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/OtherEffects/removeEffects/Metadata";
            const string RestoreDefault = "config/OtherEffects/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (others.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeadBodies()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/DeadBodies/removeEffects/Metadata";
            const string RestoreDefault = "config/DeadBodies/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (deadBodies.IsChecked)
                {
                    case true:
                        {
                            foreach (var item in remove_Effects)
                            {
                                string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            foreach (var item in restore_Default)
                            {
                                string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UiChanges()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string ReplaceUiFiles = "config/UiChanges/replaceUiFiles/Art";
            const string RestoreDefault = "config/UiChanges/restoreDefault/Art";

            if (!Directory.Exists(ReplaceUiFiles) || !Directory.Exists(RestoreDefault)) return;
            string[] replace_UiFiles = Directory.GetFiles(ReplaceUiFiles, "*.*", SearchOption.AllDirectories);
            var replace_UiFiles_path = Path.GetFileName(ReplaceUiFiles);
            int replace_UiFiles_dir = replace_UiFiles_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (uiChanges.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in replace_UiFiles)
                        {
                            string fileNames = item.Remove(0, ReplaceUiFiles.Length - replace_UiFiles_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Micro()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Micro/removeEffects/Metadata";
            const string RestoreDefault = "config/Micro/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (micro.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Custom()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Custom/removeEffects/Metadata";
            const string RestoreDefault = "config/Custom/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (custom.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetShaders()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/Shaders/removeEffects/Shaders";
            const string RestoreDefault = "config/Shaders/restoreDefault/Shaders";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (shaders.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrivateEffects()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/PrivateEffects/removeEffects/Metadata";
            const string RestoreDefault = "config/PrivateEffects/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (skillEffects.IsChecked)
                {
                    case true:
                        {
                            foreach (var item in remove_Effects)
                            {
                                string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            foreach (var item in restore_Default)
                            {
                                string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ZeroEffects()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/ZeroEffects/removeEffects/Metadata";
            const string RestoreDefault = "config/ZeroEffects/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (zeroEffects.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ZeroParticles()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/ZeroParticles/removeEffects/Shaders";
            const string RestoreDefault = "config/ZeroParticles/restoreDefault/Shaders";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (zeroParticles.IsChecked)
                {
                    case true:
                    {
                        foreach (var item in remove_Effects)
                        {
                            string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;

                    case false:
                    {
                        foreach (var item in restore_Default)
                        {
                            string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                            RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                        }
                        UpdateDisplayPanel();
                    }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BreachLeague()
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            const string RemoveEffects = "config/BreachLeague/removeEffects/Metadata";
            const string RestoreDefault = "config/BreachLeague/restoreDefault/Metadata";

            if (!Directory.Exists(RemoveEffects) || !Directory.Exists(RestoreDefault)) return;
            string[] remove_Effects = Directory.GetFiles(RemoveEffects, "*.*", SearchOption.AllDirectories);
            var remove_Effects_path = Path.GetFileName(RemoveEffects);
            int remove_Effects_dir = remove_Effects_path.Length;

            string[] restore_Default = Directory.GetFiles(RestoreDefault, "*.*", SearchOption.AllDirectories);
            var restore_Default_path = Path.GetFileName(RestoreDefault);
            int restore_Default_dir = restore_Default_path.Length;

            try
            {
                switch (breachLeague.IsChecked)
                {
                    case true:
                        {
                            foreach (var item in remove_Effects)
                            {
                                string fileNames = item.Remove(0, RemoveEffects.Length - remove_Effects_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;

                    case false:
                        {
                            foreach (var item in restore_Default)
                            {
                                string fileNames = item.Remove(0, RestoreDefault.Length - restore_Default_dir);
                                RecordsByPath[fileNames].ReplaceContents(ggpkPath, item, content.FreeRoot);
                            }
                            UpdateDisplayPanel();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Settings.Strings["ReplaceItem_Failed"], ex.Message),
                    Settings.Strings["Error_Caption"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        private void PoeSmoother_PreviewDrop(object sender, DragEventArgs e)
        {
            if (!content.IsReadOnly)
            {
                e.Effects = DragDropEffects.Link;
            }
        }

        private void PoeSmoother_Drop(object sender, DragEventArgs e)
        {
            if (content.IsReadOnly)
            {
                MessageBox.Show(Settings.Strings["ReplaceItem_Readonly"], Settings.Strings["ReplaceItem_ReadonlyCaption"]);
                return;
            }

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            // Bring-to-front hack
            Topmost = true;
            Topmost = false;

            // reset viewer to show output message
            ResetViewer();
            textBoxOutput.Text = string.Empty;
            textBoxOutput.Visibility = Visibility.Visible;

            if (MessageBox.Show(Settings.Strings["MainWindow_Window_Drop_Confirm"], 
                Settings.Strings["MainWindow_Window_Drop_Confirm_Caption"], 
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            string[] fileNames = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (fileNames == null || fileNames.Length != 1)
            {
                OutputLine(Settings.Strings["MainWindow_Drop_Failed"]);
                return;
            }

            if (Directory.Exists(fileNames[0]))
            {
                HandleDropDirectory(fileNames[0]);
            }
            else if (string.Compare(Path.GetExtension(fileNames[0]), ".zip", StringComparison.OrdinalIgnoreCase) == 0)
            {
                // Zip file
                HandleDropArchive(fileNames[0]);
            }
            else
            {
                HandleDropFile(fileNames[0]);
            }
        }

        private void PoeSmoother_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            workerThread?.Abort();
        }

        private void TriggerClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TriggerMinimize(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        private void PoeSmoother_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            switch (WindowState)
            {
                case WindowState.Maximized:
                    WindowState = WindowState.Normal;
                    break;
                case WindowState.Normal:
                    WindowState = WindowState.Maximized;
                    break;
            }
        }

        private void PoeSmoother_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}