using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The dialog service and the dialog view model behind it (W0).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DialogServiceTests
{
	private readonly FakeDialogPresenter _presenter = new();

	private DialogService Create() => new(_presenter, new ImmediateUiDispatcher());

	[Fact]
	public async Task Confirm_ReturnsTrueWhenAccepted_FalseWhenCancelled()
	{
		var service = Create();
		_presenter.Answer = d =>
		{
			d.AcceptCommand.Execute(null);
			return Task.CompletedTask;
		};
		Assert.True(await service.ConfirmAsync(new ConfirmDialogRequest("Delete", "Delete 3 sessions?", "Delete", IsDestructive: true)));

		_presenter.Answer = d =>
		{
			d.CancelCommand.Execute(null);
			return Task.CompletedTask;
		};
		Assert.False(await service.ConfirmAsync(new ConfirmDialogRequest("Delete", "Delete?")));

		var shown = _presenter.Shown[0];
		Assert.Equal(DialogKind.Confirm, shown.Kind);
		Assert.Equal("Delete 3 sessions?", shown.Message);
		Assert.Equal("Delete", shown.ConfirmText);
		Assert.True(shown.IsDestructive);
		Assert.False(_presenter.Shown[1].IsDestructive);
	}

	[Fact]
	public async Task Input_ReturnsTheText_OrNullWhenCancelled()
	{
		var service = Create();
		_presenter.Answer = d =>
		{
			Assert.Equal("old name", d.InputText);
			d.InputText = "new name";
			d.AcceptCommand.Execute(null);
			return Task.CompletedTask;
		};
		Assert.Equal("new name", await service.InputAsync(new InputDialogRequest("Rename", "Name", "old name")));

		_presenter.Answer = d =>
		{
			d.InputText = "ignored";
			d.CancelCommand.Execute(null);
			return Task.CompletedTask;
		};
		Assert.Null(await service.InputAsync(new InputDialogRequest("Rename", "Name", "old name")));
	}

	[Fact]
	public async Task Input_KeepsTheDialogOpenUntilTheValidatorAccepts()
	{
		var service = Create();
		var errorsSeen = new List<string?>();
		_presenter.Answer = d =>
		{
			d.InputText = " ";
			d.AcceptCommand.Execute(null);
			errorsSeen.Add(d.Error);
			Assert.False(d.Outcome.IsCompleted);
			Assert.True(d.HasError);

			d.InputText = "ok";
			Assert.Null(d.Error);
			d.AcceptCommand.Execute(null);
			return Task.CompletedTask;
		};

		var text = await service.InputAsync(new InputDialogRequest("Rename", "Name", Validate: t => string.IsNullOrWhiteSpace(t) ? "Name must not be empty" : null));

		Assert.Equal("ok", text);
		Assert.Equal(["Name must not be empty"], errorsSeen);
	}

	[Fact]
	public async Task Choice_ReturnsTheChosenId_OrNullWhenCancelled()
	{
		var service = Create();
		var delays = new DialogChoice[] { new("15m", "In 15 minutes"), new("1h", "In 1 hour") };
		_presenter.Answer = d =>
		{
			Assert.Equal(delays, d.Choices);
			d.ChooseCommand.Execute(d.Choices[1]);
			return Task.CompletedTask;
		};
		Assert.Equal("1h", await service.ChoiceAsync(new ChoiceDialogRequest("Delay", "Send later", delays)));

		_presenter.Answer = d =>
		{
			d.CancelCommand.Execute(null);
			return Task.CompletedTask;
		};
		Assert.Null(await service.ChoiceAsync(new ChoiceDialogRequest("Delay", "Send later", delays)));
	}

	[Fact]
	public async Task Choice_WithoutChoices_IsRejected()
	{
		await Assert.ThrowsAsync<ArgumentException>(() => Create().ChoiceAsync(new ChoiceDialogRequest("Delay", "x", [])));
	}

	[Fact]
	public async Task ConcurrentRequests_AreShownOneAfterAnother()
	{
		var service = Create();
		var gate = new TaskCompletionSource();
		_presenter.Answer = async d =>
		{
			await gate.Task;
			d.AcceptCommand.Execute(null);
		};

		var first = service.ConfirmAsync(new ConfirmDialogRequest("One", "1"));
		var second = service.ConfirmAsync(new ConfirmDialogRequest("Two", "2"));
		await Task.Delay(100);
		Assert.Single(_presenter.Shown);

		gate.SetResult();
		await Task.WhenAll(first, second);

		Assert.Equal(["One", "Two"], _presenter.Shown.Select(d => d.Title));
		Assert.Equal(1, _presenter.MaxConcurrent);
	}

	[Fact]
	public async Task APresenterFailure_ReachesTheCaller_AndDoesNotBlockTheNextDialog()
	{
		var service = Create();
		_presenter.Answer = _ => throw new InvalidOperationException("no window");
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(new ConfirmDialogRequest("A", "a")));

		_presenter.Answer = d =>
		{
			d.AcceptCommand.Execute(null);
			return Task.CompletedTask;
		};

		Assert.True(await service.ConfirmAsync(new ConfirmDialogRequest("B", "b")));
	}

	[Fact]
	public void TheFirstAnswerWins()
	{
		var dialog = new DialogViewModel(DialogKind.Confirm, "t", "m", "Yes", "No");

		dialog.AcceptCommand.Execute(null);
		dialog.CancelCommand.Execute(null);

		Assert.True(dialog.Outcome.Result.Accepted);
	}
}
