using Island.Core.Configuration;

namespace Island.Core.Tests;

public class PhoneSettingsTests
{
    [Fact]
    public void Phone_block_is_off_by_default_with_no_token()
    {
        var s = new IslandSettings();
        Assert.False(s.PhoneBlockEnabled);
        Assert.Equal(string.Empty, s.PhoneFcmToken);
    }

    [Fact]
    public void Phone_fields_take_part_in_equality_and_hash()
    {
        var a = new IslandSettings { PhoneBlockEnabled = true, PhoneFcmToken = "abc" };

        Assert.Equal(a, new IslandSettings { PhoneBlockEnabled = true, PhoneFcmToken = "abc" });
        Assert.Equal(a.GetHashCode(), new IslandSettings { PhoneBlockEnabled = true, PhoneFcmToken = "abc" }.GetHashCode());
        Assert.NotEqual(a, a with { PhoneBlockEnabled = false });
        Assert.NotEqual(a, a with { PhoneFcmToken = "xyz" });
    }
}
