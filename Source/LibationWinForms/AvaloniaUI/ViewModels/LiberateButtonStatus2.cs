﻿using Avalonia.Media.Imaging;
using DataLayer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LibationWinForms.AvaloniaUI.ViewModels
{
	public class LiberateButtonStatus2 : IComparable, INotifyPropertyChanged
	{
		public LiberatedStatus BookStatus { get; set; }
		public LiberatedStatus? PdfStatus { get; set; }

		private bool _expanded;
		public bool Expanded
		{
			get => _expanded;
			set
			{
				_expanded = value;
				NotifyPropertyChanged();
				NotifyPropertyChanged(nameof(Image));
				NotifyPropertyChanged(nameof(ToolTip));
			}
		}
		public bool IsSeries { get; init; }
		public Bitmap Image => GetLiberateIcon();
		public string ToolTip => GetTooltip();

		static Dictionary<string, Bitmap> images = new();

		public event PropertyChangedEventHandler PropertyChanged;

		public void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		/// <summary>
		/// Defines the Liberate column's sorting behavior
		/// </summary>
		public int CompareTo(object obj)
		{
			if (obj is not LiberateButtonStatus2 second) return -1;

			if (IsSeries && !second.IsSeries) return -1;
			else if (!IsSeries && second.IsSeries) return 1;
			else if (IsSeries && second.IsSeries) return 0;
			else if (BookStatus == LiberatedStatus.Liberated && second.BookStatus != LiberatedStatus.Liberated) return -1;
			else if (BookStatus != LiberatedStatus.Liberated && second.BookStatus == LiberatedStatus.Liberated) return 1;
			else return BookStatus.CompareTo(second.BookStatus);
		}


		private Bitmap GetLiberateIcon()
		{
			if (IsSeries)
				return Expanded ? GetFromresc("minus") : GetFromresc("plus");

			if (BookStatus == LiberatedStatus.Error)
				return GetFromresc("error");

			string image_lib = BookStatus switch
			{
				LiberatedStatus.Liberated => "green",
				LiberatedStatus.PartialDownload => "yellow",
				LiberatedStatus.NotLiberated => "red",
				_ => throw new Exception("Unexpected liberation state")
			};

			string image_pdf = PdfStatus switch
			{
				LiberatedStatus.Liberated => "_pdf_yes",
				LiberatedStatus.NotLiberated => "_pdf_no",
				LiberatedStatus.Error => "_pdf_no",
				null => "",
				_ => throw new Exception("Unexpected PDF state")
			};

			return GetFromresc($"liberate_{image_lib}{image_pdf}");
		}
		private string GetTooltip()
		{
			if (IsSeries)
				return Expanded ? "Click to Collpase" : "Click to Expand";

			if (BookStatus == LiberatedStatus.Error)
				return "Book downloaded ERROR";

			string libState = BookStatus switch
			{
				LiberatedStatus.Liberated => "Liberated",
				LiberatedStatus.PartialDownload => "File has been at least\r\npartially downloaded",
				LiberatedStatus.NotLiberated => "Book NOT downloaded",
				_ => throw new Exception("Unexpected liberation state")
			};

			string pdfState = PdfStatus switch
			{
				LiberatedStatus.Liberated => "\r\nPDF downloaded",
				LiberatedStatus.NotLiberated => "\r\nPDF NOT downloaded",
				LiberatedStatus.Error => "\r\nPDF downloaded ERROR",
				null => "",
				_ => throw new Exception("Unexpected PDF state")
			};


			var mouseoverText = libState + pdfState;

			if (BookStatus == LiberatedStatus.NotLiberated ||
				BookStatus == LiberatedStatus.PartialDownload ||
				PdfStatus == LiberatedStatus.NotLiberated)
				mouseoverText += "\r\nClick to complete";

			return mouseoverText;
		}

		private static Bitmap GetFromresc(string rescName)
		{
			if (images.ContainsKey(rescName)) return images[rescName];

			var memoryStream = new System.IO.MemoryStream();

			((System.Drawing.Bitmap)Properties.Resources.ResourceManager.GetObject(rescName)).Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
			memoryStream.Position = 0;
			images[rescName] = new Bitmap(memoryStream);
			return images[rescName];
		}
	}
}
