using ExtraGasMVC.Services;
using Xunit;

namespace ExtraGasMVC.Tests;

public class TemporaryPasswordGeneratorTests
{
    [Fact]
    public void Generate_DefaultLength_Is12()
    {
        var pwd = TemporaryPasswordGenerator.Generate();

        Assert.Equal(12, pwd.Length);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(16)]
    [InlineData(32)]
    public void Generate_RequestedLength_IsRespected(int length)
    {
        var pwd = TemporaryPasswordGenerator.Generate(length);

        Assert.Equal(length, pwd.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    public void Generate_BelowMinimumLength_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => TemporaryPasswordGenerator.Generate(length));
    }

    [Fact]
    public void Generate_AlwaysContainsOneLowercase()
    {
        for (var i = 0; i < 100; i++)
        {
            var pwd = TemporaryPasswordGenerator.Generate();
            Assert.Contains(pwd, c => c >= 'a' && c <= 'z');
        }
    }

    [Fact]
    public void Generate_AlwaysContainsOneUppercase()
    {
        for (var i = 0; i < 100; i++)
        {
            var pwd = TemporaryPasswordGenerator.Generate();
            Assert.Contains(pwd, c => c >= 'A' && c <= 'Z');
        }
    }

    [Fact]
    public void Generate_AlwaysContainsOneDigit()
    {
        for (var i = 0; i < 100; i++)
        {
            var pwd = TemporaryPasswordGenerator.Generate();
            Assert.Contains(pwd, c => c >= '0' && c <= '9');
        }
    }

    [Fact]
    public void Generate_AlwaysContainsOneSymbol()
    {
        var symbols = "!@#$%&*+-=?";
        for (var i = 0; i < 100; i++)
        {
            var pwd = TemporaryPasswordGenerator.Generate();
            Assert.Contains(pwd, c => symbols.Contains(c));
        }
    }

    [Fact]
    public void Generate_TwoCallsAlmostAlwaysDiffer()
    {
        // Probabilistico: con 12 chars de un alfabeto de ~70, la chance de
        // colision es astronomicamente baja. Lo corremos varias veces y
        // esperamos que TODAS sean distintas.
        var passwords = new HashSet<string>();
        for (var i = 0; i < 1000; i++)
            passwords.Add(TemporaryPasswordGenerator.Generate());

        Assert.Equal(1000, passwords.Count);
    }

    [Fact]
    public void Generate_OnlyContainsValidChars()
    {
        // Alfabeto valido segun el codigo: lower + upper + digit + symbols "!@#$%&*+-=?"
        const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%&*+-=?";
        for (var i = 0; i < 100; i++)
        {
            var pwd = TemporaryPasswordGenerator.Generate();
            foreach (var c in pwd)
                Assert.Contains(c, validChars);
        }
    }
}
