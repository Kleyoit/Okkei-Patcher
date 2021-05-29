﻿using Android.App;
using Android.OS;
using AndroidX.Lifecycle;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using OkkeiPatcher.ViewModels;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class ExitAppDialogFragment : DialogFragment
	{
		private const string TitleStringKey = "titleText";
		private const string MessageStringKey = "messageText";
		private string _title, _message;
		private MainViewModel _viewModel;

		public static ExitAppDialogFragment NewInstance(MessageData messageData)
		{
			return NewInstance(messageData.Title, messageData.Message);
		}

		public static ExitAppDialogFragment NewInstance(int titleId, int messageId)
		{
			var title = OkkeiUtils.GetText(titleId);
			var message = OkkeiUtils.GetText(messageId);

			return NewInstance(title, message);
		}

		public static ExitAppDialogFragment NewInstance(string title, string message)
		{
			var fragment = new ExitAppDialogFragment();
			var args = new Bundle();

			args.PutString(TitleStringKey, title);
			args.PutString(MessageStringKey, message);

			fragment.Arguments = args;
			return fragment;
		}

		public override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			_viewModel =
				new ViewModelProvider(RequireActivity()).Get(Java.Lang.Class.FromType(typeof(MainViewModel))) as
					MainViewModel;

			var args = Arguments;
			if (args == null) return;

			_title = args.GetString(TitleStringKey);
			_message = args.GetString(MessageStringKey);
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			_viewModel.Exiting = true;
			Cancelable = false;
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireContext())
				.SetTitle(_title)
				.SetMessage(_message)
				.SetPositiveButton(Resource.String.dialog_exit, (sender, e) => System.Environment.Exit(0))
				.Create();
		}
	}
}