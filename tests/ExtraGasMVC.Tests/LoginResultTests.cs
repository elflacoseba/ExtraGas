using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services;
using Xunit;

namespace ExtraGasMVC.Tests;

public class LoginResultTests
{
    [Fact]
    public void Ok_HasUser_Success_True()
    {
        var user = new UsuarioDto { Id = 42, Username = "jperez" };

        var result = LoginResult.Ok(user);

        Assert.True(result.Success);
        Assert.NotNull(result.User);
        Assert.Equal(42UL, result.User!.Id);
        Assert.Equal(LoginFailureReason.None, result.FailureReason);
        Assert.Equal(42UL, result.AttemptedUserId); // Ok() propaga el id
    }

    [Fact]
    public void Fail_WithAttemptedUserId_NoUser_Success_False()
    {
        var result = LoginResult.Fail(attemptedUserId: 7UL, LoginFailureReason.InvalidPassword);

        Assert.False(result.Success);
        Assert.Null(result.User);
        Assert.Equal(LoginFailureReason.InvalidPassword, result.FailureReason);
        Assert.Equal(7UL, result.AttemptedUserId); // se preserva para auditoria
    }

    [Fact]
    public void Fail_WithoutAttemptedUserId_NullId()
    {
        var result = LoginResult.Fail(attemptedUserId: null, LoginFailureReason.UserNotFound);

        Assert.False(result.Success);
        Assert.Null(result.User);
        Assert.Equal(LoginFailureReason.UserNotFound, result.FailureReason);
        Assert.Null(result.AttemptedUserId);
    }

    [Fact]
    public void FailureReasons_AreDistinct()
    {
        // Sanity: el enum tiene los valores esperados.
        Assert.Equal(6, Enum.GetValues<LoginFailureReason>().Length);
        Assert.True(Enum.IsDefined(LoginFailureReason.None));
        Assert.True(Enum.IsDefined(LoginFailureReason.UserNotFound));
        Assert.True(Enum.IsDefined(LoginFailureReason.UserInactive));
        Assert.True(Enum.IsDefined(LoginFailureReason.UserDeleted));
        Assert.True(Enum.IsDefined(LoginFailureReason.InvalidPassword));
        Assert.True(Enum.IsDefined(LoginFailureReason.LockedOut));
    }
}
