using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Package Backup Tool
/// Place in Assets/Editor/CustomPackageBackupTool.cs
/// Access via: Tools → Custom Package Backup
/// </summary>
public class CustomPackageBackupTool : EditorWindow
{
    // UI State
    private int selectedTab = 0;
    private string[] tabNames = { "Local Backup", "GitHub Backup", "Both" };

    // Package Selection
    private string[] availablePackages;
    private int selectedPackageIndex = 0;
    private Vector2 scrollPosition;

    // Local Backup
    private string backupName = "";
    private string backupLocation = "";

    // GitHub Backup
    private string repositoryName = "";
    private string branchName = "";
    private string commitMessage = "";
    private bool allowRepoOverride = false;

    // Progress
    private bool isProcessing = false;
    private string progressMessage = "";
    private float progressValue = 0f;

    // Config
    private PackageBackupConfig config;

    [MenuItem("Ali's Tools/Custom Package Backup")]
    public static void ShowWindow()
    {
        var window = GetWindow<CustomPackageBackupTool>("Package Backup Tool");
        window.minSize = new Vector2(600, 500);
    }

    void OnEnable()
    {
        config = PackageBackupConfig.Load();
        RefreshPackageList();

        // Check if setup is needed
        if (!config.IsConfigured())
        {
            if (EditorUtility.DisplayDialog(
                "Setup Required",
                "GitHub configuration not found. Would you like to set it up now?",
                "Yes, Setup Now",
                "Later"))
            {
                PackageBackupSetupWizard.ShowWizard();
            }
        }
    }

    void OnGUI()
    {
        if (isProcessing)
        {
            DrawProcessingUI();
            return;
        }

        GUILayout.Space(10);

        // Header
        GUILayout.Label("Custom Package Backup Tool", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Backup individual packages to local directory and/or GitHub repository.",
            MessageType.Info
        );

        GUILayout.Space(10);

        // Package Selection
        DrawPackageSelection();

        GUILayout.Space(10);

        // Tabs
        selectedTab = GUILayout.Toolbar(selectedTab, tabNames, GUILayout.Height(30));

        GUILayout.Space(10);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        switch (selectedTab)
        {
            case 0:
                DrawLocalBackupTab();
                break;
            case 1:
                DrawGitHubBackupTab();
                break;
            case 2:
                DrawBothBackupTab();
                break;
        }

        EditorGUILayout.EndScrollView();

        GUILayout.Space(10);

        // Footer buttons
        DrawFooterButtons();
    }

    void DrawPackageSelection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        GUILayout.Label("📦 Select Package to Backup:", EditorStyles.boldLabel);

        if (availablePackages == null || availablePackages.Length == 0)
        {
            EditorGUILayout.HelpBox("No packages found in Packages/ folder.", MessageType.Warning);
            if (GUILayout.Button("Refresh Package List"))
            {
                RefreshPackageList();
            }
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            selectedPackageIndex = EditorGUILayout.Popup("Package:", selectedPackageIndex, availablePackages);
            if (GUILayout.Button("🔄", GUILayout.Width(30)))
            {
                RefreshPackageList();
            }
            EditorGUILayout.EndHorizontal();

            // Show package info
            string selectedPackage = GetSelectedPackage();
            if (!string.IsNullOrEmpty(selectedPackage))
            {
                string packagePath = Path.Combine("Packages", selectedPackage);
                if (Directory.Exists(packagePath))
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(packagePath);
                    long size = GetDirectorySize(dirInfo);
                    string sizeStr = FormatBytes(size);

                    EditorGUILayout.LabelField("Size:", sizeStr);
                    EditorGUILayout.LabelField("Last Modified:", dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
        }

        EditorGUILayout.EndVertical();
    }

    void DrawLocalBackupTab()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("📁 Local Backup Settings", EditorStyles.boldLabel);

        backupName = EditorGUILayout.TextField("Backup Name:", backupName);
        EditorGUILayout.HelpBox(
            $"Will be saved as: {backupName}_{GetTimestamp()}/",
            MessageType.None
        );

        GUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        backupLocation = EditorGUILayout.TextField("Location:", backupLocation);
        if (GUILayout.Button("Browse", GUILayout.Width(80)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Backup Location", backupLocation, "");
            if (!string.IsNullOrEmpty(path))
            {
                backupLocation = path;
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("📁 Create Local Backup", GUILayout.Height(40)))
        {
            PerformLocalBackup();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndVertical();
    }

    void DrawGitHubBackupTab()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("🐙 GitHub Backup Settings", EditorStyles.boldLabel);

        if (!config.IsConfigured())
        {
            EditorGUILayout.HelpBox("GitHub not configured. Please configure first.", MessageType.Warning);
            if (GUILayout.Button("⚙️ Open Setup Wizard"))
            {
                PackageBackupSetupWizard.ShowWizard();
            }
            EditorGUILayout.EndVertical();
            return;
        }

        // Repository name
        string autoRepoName = GetAutoGeneratedRepoName();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Repository:");
        EditorGUILayout.SelectableLabel(autoRepoName, GUILayout.Height(18));
        allowRepoOverride = EditorGUILayout.ToggleLeft("Override", allowRepoOverride, GUILayout.Width(70));
        EditorGUILayout.EndHorizontal();

        if (allowRepoOverride)
        {
            repositoryName = EditorGUILayout.TextField("Custom Repo Name:", repositoryName);
        }
        else
        {
            repositoryName = autoRepoName;
        }

        GUILayout.Space(5);

        // Branch name
        branchName = EditorGUILayout.TextField("Branch Name:", branchName);
        if (string.IsNullOrEmpty(branchName))
        {
            branchName = backupName;
        }
        EditorGUILayout.HelpBox(
            $"Will create branch: backup/{branchName}_{GetTimestamp()}",
            MessageType.None
        );

        GUILayout.Space(5);

        // Commit message
        commitMessage = EditorGUILayout.TextField("Commit Message:", commitMessage);
        if (string.IsNullOrEmpty(commitMessage))
        {
            commitMessage = $"Backup: {GetTimestamp()}";
        }

        GUILayout.Space(10);

        // Action buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("⚙️ Configure GitHub"))
        {
            PackageBackupSetupWizard.ShowWizard();
        }
        if (GUILayout.Button("🔍 Test Connection"))
        {
            TestGitHubConnection();
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);

        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button("🔄 Push to GitHub", GUILayout.Height(40)))
        {
            PerformGitHubBackup();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndVertical();
    }

    void DrawBothBackupTab()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("📦 Backup to Both Locations", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "This will create both a local backup and push to GitHub in one operation.",
            MessageType.Info
        );

        GUILayout.Space(10);

        // Common settings
        backupName = EditorGUILayout.TextField("Backup Name:", backupName);

        GUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        backupLocation = EditorGUILayout.TextField("Local Location:", backupLocation);
        if (GUILayout.Button("Browse", GUILayout.Width(80)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Backup Location", backupLocation, "");
            if (!string.IsNullOrEmpty(path))
            {
                backupLocation = path;
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);

        commitMessage = EditorGUILayout.TextField("Commit Message:", commitMessage);

        GUILayout.Space(10);

        GUI.backgroundColor = new Color(1f, 0.7f, 0.3f);
        if (GUILayout.Button("📦 Backup Both (Local + GitHub)", GUILayout.Height(40)))
        {
            PerformBothBackup();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndVertical();
    }

    void DrawFooterButtons()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("📖 Help"))
        {
            ShowHelp();
        }

        if (GUILayout.Button("🔧 Verify Git"))
        {
            VerifyGitInstallation();
        }

        if (GUILayout.Button("⚙️ Settings"))
        {
            PackageBackupSetupWizard.ShowWizard();
        }

        EditorGUILayout.EndHorizontal();
    }

    void VerifyGitInstallation()
    {
        try
        {
            GitHubBackupManager manager = new GitHubBackupManager(config);
            string gitPath = manager.FindGitExecutable();
            string version = manager.GetGitVersion();

            EditorUtility.DisplayDialog(
                "Git Found!",
                $"✅ Git is installed and accessible!\n\nPath: {gitPath}\nVersion: {version}",
                "OK"
            );
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog(
                "Git Not Found",
                $"❌ {e.Message}",
                "OK"
            );
        }
    }

    void DrawProcessingUI()
    {
        GUILayout.FlexibleSpace();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        GUILayout.Label("Processing...", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUI.ProgressBar(
            EditorGUILayout.GetControlRect(GUILayout.Height(25)),
            progressValue,
            progressMessage
        );

        GUILayout.Space(10);

        EditorGUILayout.HelpBox("Please wait while the backup is being created...", MessageType.Info);

        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        Repaint();
    }

    void RefreshPackageList()
    {
        string packagesFolder = "Library\\PackageCache";

        if (Directory.Exists(packagesFolder))
        {
            availablePackages = Directory.GetDirectories(packagesFolder)
                .Select(path => Path.GetFileName(path))
                .Where(name => !name.StartsWith(".")) // Skip hidden folders
                .OrderBy(name => name)
                .ToArray();

            Debug.Log($"Found {availablePackages.Length} packages.");
        }
        else
        {
            availablePackages = new string[0];
            Debug.LogWarning("Packages folder not found.");
        }
    }

    string GetSelectedPackage()
    {
        if (availablePackages != null && selectedPackageIndex >= 0 && selectedPackageIndex < availablePackages.Length)
        {
            return availablePackages[selectedPackageIndex];
        }
        return null;
    }

    string GetAutoGeneratedRepoName()
    {
        string packageName = GetSelectedPackage();
        if (string.IsNullOrEmpty(packageName))
            return "package-backup";

        return $"{packageName}-backup";
    }

    string GetTimestamp()
    {
        return DateTime.Now.ToString("yyyy-MM-dd-HH-mm");
    }

    async void PerformLocalBackup()
    {
        string packageName = GetSelectedPackage();
        if (string.IsNullOrEmpty(packageName))
        {
            EditorUtility.DisplayDialog("Error", "Please select a package first.", "OK");
            return;
        }

        if (string.IsNullOrEmpty(backupName))
        {
            EditorUtility.DisplayDialog("Error", "Please enter a backup name.", "OK");
            return;
        }

        if (string.IsNullOrEmpty(backupLocation))
        {
            EditorUtility.DisplayDialog("Error", "Please select a backup location.", "OK");
            return;
        }

        isProcessing = true;
        progressMessage = "Creating local backup...";
        progressValue = 0f;

        try
        {
            string sourcePath = Path.Combine("Packages", packageName);
            string destFolderName = $"{backupName}_{GetTimestamp()}";
            string destPath = Path.Combine(backupLocation, destFolderName);

            progressValue = 0.3f;
            progressMessage = "Copying files...";

            await Task.Run(() => CopyDirectory(sourcePath, destPath));

            progressValue = 1f;
            progressMessage = "Complete!";

            Debug.Log($"✅ Local backup created: {destPath}");

            EditorUtility.DisplayDialog(
                "Success!",
                $"Local backup created successfully!\n\nLocation: {destPath}",
                "OK"
            );
        }
        catch (Exception e)
        {
            Debug.LogError($"Local backup failed: {e.Message}");
            EditorUtility.DisplayDialog("Error", $"Local backup failed:\n{e.Message}", "OK");
        }
        finally
        {
            isProcessing = false;
            progressValue = 0f;
        }
    }

    async void PerformGitHubBackup()
    {
        string packageName = GetSelectedPackage();
        if (string.IsNullOrEmpty(packageName))
        {
            EditorUtility.DisplayDialog("Error", "Please select a package first.", "OK");
            return;
        }

        if (!config.IsConfigured())
        {
            EditorUtility.DisplayDialog("Error", "GitHub not configured. Please run Setup Wizard first.", "OK");
            return;
        }

        isProcessing = true;
        progressMessage = "Initializing GitHub backup...";
        progressValue = 0f;

        try
        {
            string sourcePath = Path.Combine("Library\\PackageCache", packageName);
            string finalBranchName = $"backup/{branchName}_{GetTimestamp()}";
            string finalCommitMessage = $"{commitMessage} - {GetTimestamp()}";

            GitHubBackupManager manager = new GitHubBackupManager(config);

            progressValue = 0.2f;
            progressMessage = "Checking/creating repository...";

            bool repoExists = await manager.EnsureRepositoryExists(repositoryName);

            progressValue = 0.4f;
            progressMessage = "Creating local git repository...";

            await manager.InitializeLocalRepo(sourcePath, repositoryName, finalBranchName, finalCommitMessage);

            progressValue = 0.8f;
            progressMessage = "Pushing to GitHub...";

            await manager.PushToGitHub(sourcePath, repositoryName, finalBranchName);

            progressValue = 1f;
            progressMessage = "Complete!";

            Debug.Log($"✅ GitHub backup complete: {config.GitHubUsername}/{repositoryName} @ {finalBranchName}");

            EditorUtility.DisplayDialog(
                "Success!",
                $"GitHub backup complete!\n\nRepository: {repositoryName}\nBranch: {finalBranchName}",
                "OK"
            );
        }
        catch (Exception e)
        {
            Debug.LogError($"GitHub backup failed: {e.Message}");
            EditorUtility.DisplayDialog("Error", $"GitHub backup failed:\n{e.Message}", "OK");
        }
        finally
        {
            isProcessing = false;
            progressValue = 0f;
        }
    }

    async void PerformBothBackup()
    {
        string packageName = GetSelectedPackage();
        if (string.IsNullOrEmpty(packageName))
        {
            EditorUtility.DisplayDialog("Error", "Please select a package first.", "OK");
            return;
        }

        if (string.IsNullOrEmpty(backupName))
        {
            EditorUtility.DisplayDialog("Error", "Please enter a backup name.", "OK");
            return;
        }

        isProcessing = true;
        bool localSuccess = false;

        try
        {
            // Local backup first
            progressMessage = "Creating local backup...";
            progressValue = 0.1f;

            string sourcePath = Path.Combine("Packages", packageName);
            string destFolderName = $"{backupName}_{GetTimestamp()}";
            string destPath = Path.Combine(backupLocation, destFolderName);

            await Task.Run(() => CopyDirectory(sourcePath, destPath));

            progressValue = 0.4f;
            localSuccess = true;

            Debug.Log($"✅ Local backup created: {destPath}");

            // GitHub backup
            if (config.IsConfigured())
            {
                progressMessage = "Pushing to GitHub...";
                progressValue = 0.5f;

                string finalBranchName = $"backup/{branchName}_{GetTimestamp()}";
                string finalCommitMessage = $"{commitMessage} - {GetTimestamp()}";

                GitHubBackupManager manager = new GitHubBackupManager(config);

                await manager.EnsureRepositoryExists(repositoryName);
                progressValue = 0.6f;

                await manager.InitializeLocalRepo(sourcePath, repositoryName, finalBranchName, finalCommitMessage);
                progressValue = 0.8f;

                await manager.PushToGitHub(sourcePath, repositoryName, finalBranchName);
                progressValue = 1f;

                Debug.Log($"✅ GitHub backup complete: {config.GitHubUsername}/{repositoryName}");

                EditorUtility.DisplayDialog(
                    "Success!",
                    $"Both backups created successfully!\n\nLocal: {destPath}\nGitHub: {repositoryName} @ {finalBranchName}",
                    "OK"
                );
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Partial Success",
                    $"Local backup created successfully, but GitHub is not configured.\n\nLocal: {destPath}",
                    "OK"
                );
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Backup failed: {e.Message}");

            if (localSuccess)
            {
                EditorUtility.DisplayDialog(
                    "Partial Success",
                    $"Local backup succeeded, but GitHub backup failed:\n{e.Message}",
                    "OK"
                );
            }
            else
            {
                EditorUtility.DisplayDialog("Error", $"Backup failed:\n{e.Message}", "OK");
            }
        }
        finally
        {
            isProcessing = false;
            progressValue = 0f;
        }
    }

    async void TestGitHubConnection()
    {
        if (!config.IsConfigured())
        {
            EditorUtility.DisplayDialog("Error", "GitHub not configured.", "OK");
            return;
        }

        isProcessing = true;
        progressMessage = "Testing GitHub connection...";
        progressValue = 0.5f;

        try
        {
            GitHubBackupManager manager = new GitHubBackupManager(config);
            bool success = await manager.TestConnection();

            if (success)
            {
                EditorUtility.DisplayDialog(
                    "Success!",
                    $"✅ Connected to GitHub as: {config.GitHubUsername}\n\nToken has correct permissions.",
                    "OK"
                );
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Connection Failed",
                    "Could not connect to GitHub. Please check your credentials.",
                    "OK"
                );
            }
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Error", $"Connection test failed:\n{e.Message}", "OK");
        }
        finally
        {
            isProcessing = false;
            progressValue = 0f;
        }
    }

    void ShowHelp()
    {
        string help = @"Custom Package Backup Tool - Help

USAGE:
1. Select a package from the dropdown
2. Choose a backup method:
   - Local: Saves to your computer
   - GitHub: Pushes to GitHub repository
   - Both: Does both in one operation

LOCAL BACKUP:
- Enter a backup name
- Choose a location
- Click 'Create Local Backup'
- Files will be saved as: {name}_{timestamp}/

GITHUB BACKUP:
- Configure GitHub credentials first (Settings button)
- Repository name is auto-generated
- Branch is created as: backup/{name}_{timestamp}
- Click 'Push to GitHub'

BOTH:
- Creates local backup first
- Then pushes to GitHub
- If GitHub fails, local backup is still saved

TIPS:
- Use descriptive backup names
- Test GitHub connection before first backup
- Keep your PAT token secure
- Branch names help organize backups";

        EditorUtility.DisplayDialog("Help", help, "OK");
    }

    void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (string file in Directory.GetFiles(source))
        {
            string fileName = Path.GetFileName(file);
            File.Copy(file, Path.Combine(dest, fileName), true);
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            string dirName = Path.GetFileName(dir);
            CopyDirectory(dir, Path.Combine(dest, dirName));
        }
    }

    long GetDirectorySize(DirectoryInfo dirInfo)
    {
        long size = 0;

        FileInfo[] files = dirInfo.GetFiles();
        foreach (FileInfo file in files)
        {
            size += file.Length;
        }

        DirectoryInfo[] dirs = dirInfo.GetDirectories();
        foreach (DirectoryInfo dir in dirs)
        {
            size += GetDirectorySize(dir);
        }

        return size;
    }

    string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

// ============================================================
// Config Manager
// ============================================================
[Serializable]
public class PackageBackupConfig
{
    private static string ConfigFilePath = ".packagebackup.config";

    public string GitHubUsername;
    public string GitHubToken;
    public string DefaultBranch = "backup";

    public bool IsConfigured()
    {
        return !string.IsNullOrEmpty(GitHubUsername) && !string.IsNullOrEmpty(GitHubToken);
    }

    public static PackageBackupConfig Load()
    {
        if (File.Exists(ConfigFilePath))
        {
            try
            {
                string[] lines = File.ReadAllLines(ConfigFilePath);
                PackageBackupConfig config = new PackageBackupConfig();

                foreach (string line in lines)
                {
                    if (line.StartsWith("username="))
                        config.GitHubUsername = line.Substring(9).Trim();
                    else if (line.StartsWith("token="))
                        config.GitHubToken = line.Substring(6).Trim();
                    else if (line.StartsWith("default-branch="))
                        config.DefaultBranch = line.Substring(15).Trim();
                }

                return config;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load config: {e.Message}");
            }
        }

        return new PackageBackupConfig();
    }

    public void Save()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[github]");
            sb.AppendLine($"username={GitHubUsername}");
            sb.AppendLine($"token={GitHubToken}");
            sb.AppendLine($"default-branch={DefaultBranch}");

            File.WriteAllText(ConfigFilePath, sb.ToString());

            Debug.Log($"✅ Configuration saved to {ConfigFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save config: {e.Message}");
        }
    }
}

// ============================================================
// GitHub Backup Manager
// ============================================================
public class GitHubBackupManager
{
    private PackageBackupConfig config;
    private static readonly HttpClient httpClient = new HttpClient();
    private string gitExecutablePath = null;

    public GitHubBackupManager(PackageBackupConfig config)
    {
        this.config = config;

        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Unity-Package-Backup-Tool");
        httpClient.DefaultRequestHeaders.Add("Authorization", $"token {config.GitHubToken}");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
    }

    public string FindGitExecutable()
    {
        if (gitExecutablePath != null)
            return gitExecutablePath;

        // Common Git installation paths
        string[] possiblePaths = new string[]
        {
            "git", // If in PATH
            @"C:\Program Files\Git\bin\git.exe",
            @"C:\Program Files (x86)\Git\bin\git.exe",
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files (x86)\Git\cmd\git.exe",
            @"C:\Users\" + Environment.UserName + @"\AppData\Local\Programs\Git\bin\git.exe",
            @"C:\Users\" + Environment.UserName + @"\AppData\Local\Programs\Git\cmd\git.exe"
        };

        foreach (string path in possiblePaths)
        {
            try
            {
                if (path == "git")
                {
                    // Test if git is in PATH
                    System.Diagnostics.Process testProcess = new System.Diagnostics.Process();
                    testProcess.StartInfo.FileName = "git";
                    testProcess.StartInfo.Arguments = "--version";
                    testProcess.StartInfo.UseShellExecute = false;
                    testProcess.StartInfo.RedirectStandardOutput = true;
                    testProcess.StartInfo.RedirectStandardError = true;
                    testProcess.StartInfo.CreateNoWindow = true;
                    testProcess.Start();
                    testProcess.WaitForExit();

                    if (testProcess.ExitCode == 0)
                    {
                        gitExecutablePath = "git";
                        return gitExecutablePath;
                    }
                }
                else if (File.Exists(path))
                {
                    gitExecutablePath = path;
                    return gitExecutablePath;
                }
            }
            catch
            {
                continue;
            }
        }

        throw new Exception(
            "Git not found! Please install Git or add it to your system PATH.\n\n" +
            "Download Git from: https://git-scm.com/download/win\n\n" +
            "After installation, restart Unity."
        );
    }

    public string GetGitVersion()
    {
        try
        {
            return RunGitCommand(Directory.GetCurrentDirectory(), "--version").Trim();
        }
        catch
        {
            return "Unknown";
        }
    }

    public async Task<bool> TestConnection()
    {
        try
        {
            var response = await httpClient.GetAsync("https://api.github.com/user");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnsureRepositoryExists(string repoName)
    {
        try
        {
            // Check if repo exists
            var checkResponse = await httpClient.GetAsync($"https://api.github.com/repos/{config.GitHubUsername}/{repoName}");

            if (checkResponse.IsSuccessStatusCode)
            {
                Debug.Log($"Repository {repoName} already exists.");
                return true;
            }

            // Create new repo
            var createData = new
            {
                name = repoName,
                description = "Unity Package Backup",
                @private = true,
                auto_init = true
            };

            string json = JsonConvert.SerializeObject(createData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var createResponse = await httpClient.PostAsync("https://api.github.com/user/repos", content);

            if (createResponse.IsSuccessStatusCode)
            {
                Debug.Log($"✅ Created new repository: {repoName}");
                await Task.Delay(2000); // Wait for GitHub to process
                return true;
            }
            else
            {
                string error = await createResponse.Content.ReadAsStringAsync();
                throw new Exception($"Failed to create repository: {error}");
            }
        }
        catch (Exception e)
        {
            throw new Exception($"Repository check/creation failed: {e.Message}");
        }
    }

    public async Task InitializeLocalRepo(string packagePath, string repoName, string branchName, string commitMessage)
    {
        await Task.Run(() =>
        {
            try
            {
                // Initialize git if not already
                if (!Directory.Exists(Path.Combine(packagePath, ".git")))
                {
                    RunGitCommand(packagePath, "init");
                    RunGitCommand(packagePath, $"remote add origin https://github.com/{config.GitHubUsername}/{repoName}.git");
                }

                // Configure git
                RunGitCommand(packagePath, $"config user.name \"{config.GitHubUsername}\"");
                RunGitCommand(packagePath, $"config user.email \"{config.GitHubUsername}@users.noreply.github.com\"");

                // Create and checkout new branch
                RunGitCommand(packagePath, $"checkout -b {branchName}");

                // Add all files
                RunGitCommand(packagePath, "add .");

                // Commit
                RunGitCommand(packagePath, $"commit -m \"{commitMessage}\"");

                Debug.Log($"✅ Local git repository initialized on branch: {branchName}");
            }
            catch (Exception e)
            {
                throw new Exception($"Git initialization failed: {e.Message}");
            }
        });
    }

    public async Task PushToGitHub(string packagePath, string repoName, string branchName)
    {
        await Task.Run(() =>
        {
            try
            {
                // Set remote URL with token
                string remoteUrl = $"https://{config.GitHubToken}@github.com/{config.GitHubUsername}/{repoName}.git";
                RunGitCommand(packagePath, $"remote set-url origin {remoteUrl}");

                // Push
                RunGitCommand(packagePath, $"push -u origin {branchName}");

                Debug.Log($"✅ Pushed to GitHub: {repoName} @ {branchName}");
            }
            catch (Exception e)
            {
                throw new Exception($"Git push failed: {e.Message}");
            }
        });
    }

    private string RunGitCommand(string workingDirectory, string arguments)
    {
        string gitPath = FindGitExecutable();

        System.Diagnostics.Process process = new System.Diagnostics.Process();
        process.StartInfo.FileName = gitPath;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
        {
            throw new Exception($"Git command failed: {error}");
        }

        return output;
    }
}

// ============================================================
// Setup Wizard
// ============================================================
public class PackageBackupSetupWizard : EditorWindow
{
    private string username = "";
    private string token = "";
    private string defaultBranch = "backup";
    private int currentStep = 0;

    public static void ShowWizard()
    {
        var window = GetWindow<PackageBackupSetupWizard>("Setup Wizard");
        window.minSize = new Vector2(500, 400);

        // Load existing config
        PackageBackupConfig config = PackageBackupConfig.Load();
        window.username = config.GitHubUsername ?? "";
        window.token = config.GitHubToken ?? "";
        window.defaultBranch = config.DefaultBranch ?? "backup";
    }

    void OnGUI()
    {
        GUILayout.Space(20);

        GUILayout.Label("GitHub Configuration Setup", EditorStyles.boldLabel);

        GUILayout.Space(20);

        switch (currentStep)
        {
            case 0:
                DrawWelcomeStep();
                break;
            case 1:
                DrawCredentialsStep();
                break;
            case 2:
                DrawTestStep();
                break;
            case 3:
                DrawCompleteStep();
                break;
        }
    }

    void DrawWelcomeStep()
    {
        EditorGUILayout.HelpBox(
            "Welcome to the Package Backup Tool Setup!\n\n" +
            "This wizard will help you configure GitHub integration for automatic package backups.\n\n" +
            "You will need:\n" +
            "• GitHub username\n" +
            "• Personal Access Token (PAT) with 'repo' permissions",
            MessageType.Info
        );

        GUILayout.Space(20);

        if (GUILayout.Button("How to create a Personal Access Token?", GUILayout.Height(30)))
        {
            Application.OpenURL("https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token");
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Next →", GUILayout.Height(40)))
        {
            currentStep = 1;
        }
    }

    void DrawCredentialsStep()
    {
        EditorGUILayout.HelpBox("Enter your GitHub credentials:", MessageType.Info);

        GUILayout.Space(10);

        username = EditorGUILayout.TextField("GitHub Username:", username);
        token = EditorGUILayout.PasswordField("Personal Access Token:", token);
        defaultBranch = EditorGUILayout.TextField("Default Branch Prefix:", defaultBranch);

        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "⚠️ Note: The token will be stored in plain text in .packagebackup.config\n" +
            "Make sure to add this file to your .gitignore!",
            MessageType.Warning
        );

        GUILayout.FlexibleSpace();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("← Back", GUILayout.Height(40)))
        {
            currentStep = 0;
        }

        GUI.enabled = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(token);
        if (GUILayout.Button("Test Connection →", GUILayout.Height(40)))
        {
            currentStep = 2;
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
    }

    async void DrawTestStep()
    {
        EditorGUILayout.HelpBox("Testing GitHub connection...", MessageType.Info);

        GUILayout.Space(20);

        EditorGUILayout.LabelField("Username:", username);
        EditorGUILayout.LabelField("Token:", new string('*', token.Length));

        GUILayout.Space(20);

        if (GUILayout.Button("🔍 Test Now", GUILayout.Height(40)))
        {
            await TestConnection();
        }

        GUILayout.FlexibleSpace();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("← Back", GUILayout.Height(40)))
        {
            currentStep = 1;
        }

        if (GUILayout.Button("Save & Finish", GUILayout.Height(40)))
        {
            SaveConfiguration();
            currentStep = 3;
        }
        EditorGUILayout.EndHorizontal();
    }

    void DrawCompleteStep()
    {
        EditorGUILayout.HelpBox(
            "✅ Setup Complete!\n\n" +
            "GitHub integration is now configured.\n\n" +
            "You can now use the Package Backup Tool to backup your packages to GitHub.",
            MessageType.Info
        );

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Open Backup Tool", GUILayout.Height(40)))
        {
            CustomPackageBackupTool.ShowWindow();
            Close();
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Close", GUILayout.Height(30)))
        {
            Close();
        }
    }

    async Task TestConnection()
    {
        try
        {
            PackageBackupConfig tempConfig = new PackageBackupConfig
            {
                GitHubUsername = username,
                GitHubToken = token
            };

            GitHubBackupManager manager = new GitHubBackupManager(tempConfig);
            bool success = await manager.TestConnection();

            if (success)
            {
                EditorUtility.DisplayDialog(
                    "Success!",
                    "✅ Connection successful!\n\nYour credentials are valid.",
                    "OK"
                );
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Failed",
                    "❌ Connection failed!\n\nPlease check your credentials and try again.",
                    "OK"
                );
            }
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Error", $"Test failed:\n{e.Message}", "OK");
        }
    }

    void SaveConfiguration()
    {
        PackageBackupConfig config = new PackageBackupConfig
        {
            GitHubUsername = username,
            GitHubToken = token,
            DefaultBranch = defaultBranch
        };

        config.Save();

        // Create .gitignore entry
        string gitignorePath = ".gitignore";
        string ignoreEntry = ".packagebackup.config";

        if (File.Exists(gitignorePath))
        {
            string content = File.ReadAllText(gitignorePath);
            if (!content.Contains(ignoreEntry))
            {
                File.AppendAllText(gitignorePath, $"\n{ignoreEntry}\n");
            }
        }
        else
        {
            File.WriteAllText(gitignorePath, $"{ignoreEntry}\n");
        }

        Debug.Log("✅ Configuration saved successfully!");
    }
}
