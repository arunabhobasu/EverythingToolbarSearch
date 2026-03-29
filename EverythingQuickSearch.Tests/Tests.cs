using EverythingQuickSearch;
using EverythingQuickSearch.Util;
using Microsoft.Win32;
using Xunit;

namespace EverythingQuickSearch.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SearchCategory"/>.
    /// </summary>
    public class SearchCategoryTests
    {
        [Fact]
        public void GetExtensions_Image_ContainsExtPrefix()
        {
            var result = SearchCategory.GetExtensions(Category.Image);
            Assert.Contains("ext:", result);
        }

        [Fact]
        public void GetExtensions_All_ReturnsEmptyString()
        {
            var result = SearchCategory.GetExtensions(Category.All);
            Assert.Equal(string.Empty, result);
        }

        [Theory]
        [InlineData(Category.Image)]
        [InlineData(Category.Document)]
        [InlineData(Category.Audio)]
        [InlineData(Category.Video)]
        [InlineData(Category.Executable)]
        [InlineData(Category.Compressed)]
        public void GetExtensions_NonAllCategory_ReturnsNonEmptyString(Category category)
        {
            var result = SearchCategory.GetExtensions(category);
            Assert.False(string.IsNullOrEmpty(result));
        }
    }

    /// <summary>
    /// Unit tests for <see cref="RegistryHelper"/> using a unique test key.
    /// </summary>
    public class RegistryHelperTests : IDisposable
    {
        private const string TestKeyName = "EverythingQuickSearch_UnitTests_" + nameof(RegistryHelperTests);
        private readonly RegistryHelper _helper = new RegistryHelper(TestKeyName);

        [Fact]
        public void WriteAndRead_Bool_RoundTrips()
        {
            _helper.WriteToRegistryRoot("TestBool", true);
            var result = _helper.ReadKeyValueRoot("TestBool");
            Assert.NotNull(result);
            Assert.IsType<bool>(result);
            Assert.True((bool)result);
        }

        [Fact]
        public void WriteAndRead_FalseBool_RoundTrips()
        {
            _helper.WriteToRegistryRoot("TestBoolFalse", false);
            var result = _helper.ReadKeyValueRoot("TestBoolFalse");
            Assert.NotNull(result);
            Assert.IsType<bool>(result);
            Assert.False((bool)result);
        }

        [Fact]
        public void KeyExistsRoot_MissingKey_ReturnsFalse()
        {
            bool exists = _helper.KeyExistsRoot("NonExistentKey_XYZ");
            Assert.False(exists);
        }

        [Fact]
        public void KeyExistsRoot_AfterWrite_ReturnsTrue()
        {
            _helper.WriteToRegistryRoot("ExistenceTestKey", 42);
            bool exists = _helper.KeyExistsRoot("ExistenceTestKey");
            Assert.True(exists);
        }

        public void Dispose()
        {
            // Clean up the test registry key.
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree($"SOFTWARE\\{TestKeyName}", throwOnMissingSubKey: false);
            }
            catch { }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EverythingInstaller"/> pure helpers.
    /// </summary>
    public class EverythingInstallerTests
    {
        [Fact]
        public void FindEverythingExe_WithNonExistentPaths_ReturnsNull()
        {
            // We cannot easily inject paths, but we can verify that FindEverythingExe
            // returns null when called in this environment where Everything isn't installed.
            // This test validates the method runs without throwing.
            var result = EverythingInstaller.FindEverythingExe();
            // On the CI test runner (Linux or clean Windows), Everything is not installed.
            // The result is either null (not found) or a valid path (if installed).
            // We just verify it doesn't throw.
            Assert.True(result == null || System.IO.File.Exists(result));
        }

        [Fact]
        public void IsEverythingInstalled_DoesNotThrow()
        {
            // Validate that IsEverythingInstalled runs without throwing.
            bool result = EverythingInstaller.IsEverythingInstalled();
            Assert.IsType<bool>(result);
        }

        [Fact]
        public void IsEverythingRunning_DoesNotThrow()
        {
            bool result = EverythingInstaller.IsEverythingRunning();
            Assert.IsType<bool>(result);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="Core.Settings"/>.
    /// These tests use a unique registry key to avoid polluting real settings.
    /// </summary>
    public class SettingsTests : IDisposable
    {
        private const string TestKeyName = "EverythingQuickSearch_UnitTests_" + nameof(SettingsTests);

        // We can't easily inject the registry key into Settings without changing its constructor,
        // so we test the RegistryHelper that Settings relies on, which is the pure logic.
        private readonly RegistryHelper _helper = new RegistryHelper(TestKeyName);

        [Fact]
        public void RegistryHelper_DefaultRead_ReturnsNull_WhenKeyAbsent()
        {
            // A fresh key should have no value for an unwritten property.
            var result = _helper.ReadKeyValueRoot("TransparentBackground_NotSet");
            Assert.Null(result);
        }

        [Fact]
        public void RegistryHelper_WriteBool_CanBeReadBack()
        {
            _helper.WriteToRegistryRoot("TransparentBackground", true);
            var result = _helper.ReadKeyValueRoot("TransparentBackground");
            Assert.NotNull(result);
            Assert.True((bool)result);
        }

        public void Dispose()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree($"SOFTWARE\\{TestKeyName}", throwOnMissingSubKey: false);
            }
            catch { }
        }
    }
}
