using UniGetUI.Avalonia.ViewModels;
using UniGetUI.Core.Data;
using UniGetUI.Core.SettingsEngine;
using UniGetUI.PackageEngine.Classes.Packages.Classes;

namespace UniGetUI.Tests;

public sealed class ManageShortcutsViewModelTests : IDisposable
{
    private const string PackageId = "TestManager\\Contoso.Tool";
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        nameof(ManageShortcutsViewModelTests),
        Guid.NewGuid().ToString("N")
    );
    private readonly string _shortcut;

    public ManageShortcutsViewModelTests()
    {
        string userPrograms = Path.Combine(_testRoot, "User", "Programs");
        string commonPrograms = Path.Combine(_testRoot, "Common", "Programs");
        Directory.CreateDirectory(userPrograms);
        Directory.CreateDirectory(commonPrograms);

        CoreData.TEST_DataDirectoryOverride = Path.Combine(_testRoot, "Data");
        Directory.CreateDirectory(CoreData.UniGetUIUserConfigurationDirectory);
        Settings.ResetSettings();
        StartMenuShortcutsDatabase.TEST_UserProgramsOverride = userPrograms;
        StartMenuShortcutsDatabase.TEST_CommonProgramsOverride = commonPrograms;

        _shortcut = Path.Combine(userPrograms, "Contoso Tool.lnk");
        File.WriteAllText(_shortcut, "shortcut");
        StartMenuShortcutsDatabase.MarkPending(PackageId, _shortcut);
    }

    public void Dispose()
    {
        Settings.ResetSettings();
        StartMenuShortcutsDatabase.TEST_UserProgramsOverride = null;
        StartMenuShortcutsDatabase.TEST_CommonProgramsOverride = null;
        StartMenuLocation.Reset();
        CoreData.TEST_DataDirectoryOverride = null;
        Directory.Delete(_testRoot, recursive: true);
    }

    private static KeyValuePair<string, string>[] SnapshotSettings() =>
        Directory.GetFiles(CoreData.UniGetUIUserConfigurationDirectory)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new KeyValuePair<string, string>(path, File.ReadAllText(path)))
            .ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingFolderRowWithoutSavingPreservesRulesAndPendingShortcuts(bool hasRule)
    {
        if (hasRule)
            StartMenuShortcutsDatabase.SetRule(PackageId, "Dev Tools");

        var before = SnapshotSettings();
        var dialog = new ManageShortcutsViewModel(scope: ShortcutDialogScope.StartMenu);
        var rule = Assert.Single(dialog.StartMenuRules);

        rule.RemoveCommand.Execute(null);

        Assert.Empty(dialog.StartMenuRules);
        Assert.False(dialog.HasStartMenuRules);
        Assert.Equal(before, SnapshotSettings());
        Assert.Equal(hasRule ? "Dev Tools" : null, StartMenuShortcutsDatabase.GetRule(PackageId));
        Assert.Equal((PackageId, _shortcut), Assert.Single(StartMenuShortcutsDatabase.GetPendingShortcuts()));
        Assert.Equal("shortcut", File.ReadAllText(_shortcut));

        // Closing the window has no save callback; a new dialog must show the persisted row.
        var reopened = new ManageShortcutsViewModel(scope: ShortcutDialogScope.StartMenu);
        Assert.Equal(PackageId, Assert.Single(reopened.StartMenuRules).PackageId);
    }

    [Fact]
    public void SavingCommitsOnlyTheRemovedRuleAndItsPendingEntries()
    {
        const string otherPackage = "TestManager\\Fabrikam.Tool";
        StartMenuShortcutsDatabase.SetRule(PackageId, "Dev Tools");
        StartMenuShortcutsDatabase.SetRule(otherPackage, "Other Tools");
        var dialog = new ManageShortcutsViewModel(scope: ShortcutDialogScope.StartMenu);
        var rule = Assert.Single(dialog.StartMenuRules, rule => rule.PackageId == PackageId);

        rule.RemoveCommand.Execute(null);
        Assert.Equal("Dev Tools", StartMenuShortcutsDatabase.GetRule(PackageId));
        Assert.Single(StartMenuShortcutsDatabase.GetPendingShortcuts());

        dialog.SaveChanges();

        Assert.Null(StartMenuShortcutsDatabase.GetRule(PackageId));
        Assert.Equal("Other Tools", StartMenuShortcutsDatabase.GetRule(otherPackage));
        Assert.Empty(StartMenuShortcutsDatabase.GetPendingShortcuts());
        Assert.Equal("shortcut", File.ReadAllText(_shortcut));
    }

    [Fact]
    public void FailedSaveValidationKeepsRemovalsPendingUntilASuccessfulSave()
    {
        const string otherPackage = "TestManager\\Fabrikam.Tool";
        StartMenuShortcutsDatabase.SetRule(PackageId, "Dev Tools");
        StartMenuShortcutsDatabase.SetRule(otherPackage, "Other Tools");
        var dialog = new ManageShortcutsViewModel(scope: ShortcutDialogScope.StartMenu);
        var removedRule = Assert.Single(dialog.StartMenuRules, rule => rule.PackageId == PackageId);
        var otherRule = Assert.Single(dialog.StartMenuRules, rule => rule.PackageId == otherPackage);
        bool closed = false;
        dialog.CloseRequested += (_, _) => closed = true;

        removedRule.RemoveCommand.Execute(null);
        otherRule.Folder = "..";
        dialog.SaveAndClose();

        Assert.False(closed);
        Assert.Equal("Dev Tools", StartMenuShortcutsDatabase.GetRule(PackageId));
        Assert.Single(StartMenuShortcutsDatabase.GetPendingShortcuts());

        otherRule.Folder = "Other Tools";
        dialog.SaveAndClose();

        Assert.True(closed);
        Assert.Null(StartMenuShortcutsDatabase.GetRule(PackageId));
        Assert.Empty(StartMenuShortcutsDatabase.GetPendingShortcuts());
        Assert.Equal("shortcut", File.ReadAllText(_shortcut));
    }
}
