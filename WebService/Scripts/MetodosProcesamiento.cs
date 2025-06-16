using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web;

namespace WebService.Scripts
{
	public class ResultadoMomentosHu
	{
		public double[] Moments { get; set; }
		public PointF Center { get; set; }
	}

	public static class MetodosProcesamiento
	{
		public static Bitmap Escala_Grises(Bitmap btm)
		{
			Bitmap imagenFormato = new Bitmap(btm.Width, btm.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(imagenFormato))
			{
				g.DrawImage(btm, new Rectangle(0, 0, btm.Width, btm.Height));
			}

			BitmapData bmpdata = imagenFormato.LockBits(
				new Rectangle(0, 0, imagenFormato.Width, imagenFormato.Height),
				ImageLockMode.ReadWrite,
				imagenFormato.PixelFormat
			);

			int numbytes = bmpdata.Stride * imagenFormato.Height;
			byte[] bytedata = new byte[numbytes];
			IntPtr arregloImagen = bmpdata.Scan0;

			Marshal.Copy(arregloImagen, bytedata, 0, numbytes);

			for (int y = 0; y < imagenFormato.Height; y++)
			{
				int currentLine = y * bmpdata.Stride;
				for (int x = 0; x < imagenFormato.Width; x++)
				{
					int currentPixel = currentLine + x * 4;

					byte B = bytedata[currentPixel];
					byte G = bytedata[currentPixel + 1];
					byte R = bytedata[currentPixel + 2];

					byte gris = (byte)(0.299 * R + 0.587 * G + 0.114 * B);

					bytedata[currentPixel] = bytedata[currentPixel + 1] = bytedata[currentPixel + 2] = gris;
				}
			}

			Marshal.Copy(bytedata, 0, arregloImagen, numbytes);
			imagenFormato.UnlockBits(bmpdata);

			return imagenFormato;
		}
		public static Bitmap Binarizar(Bitmap btm)
		{
			btm = Escala_Grises(btm);
			BitmapData bmpdata = btm.LockBits(new Rectangle(0, 0, btm.Width, btm.Height),
											ImageLockMode.ReadWrite, btm.PixelFormat);
			int numbytes = bmpdata.Stride * btm.Height;
			byte[] bytedata = new byte[numbytes];
			IntPtr arregloImagen = bmpdata.Scan0;

			Marshal.Copy(arregloImagen, bytedata, 0, numbytes);

			long sum = 0;
			int pixelCount = 0;

			for (int i = 0; i < numbytes; i += 4)
			{
				sum += bytedata[i];
				pixelCount++;
			}

			byte promedioBrillo = (byte)(sum / pixelCount);
			byte umbral = CalcularUmbralOtsu(bytedata, btm.Width, btm.Height, bmpdata.Stride);
			bool objetoEsOscuro = promedioBrillo < umbral;

			for (int i = 0; i < numbytes; i += 4)
			{
				byte bwValue;

				if (objetoEsOscuro)
					bwValue = bytedata[i] > umbral ? (byte)255 : (byte)0;
				else
					bwValue = bytedata[i] > umbral ? (byte)0 : (byte)255;

				bytedata[i] = bytedata[i + 1] = bytedata[i + 2] = bwValue;
			}

			Marshal.Copy(bytedata, 0, arregloImagen, numbytes);
			btm.UnlockBits(bmpdata);
			int tamanoKernel = 5;
			Bitmap imagenCerrada = Cierre(btm, tamanoKernel);

			return imagenCerrada;
		}
		public static Bitmap Detectar_Bordes(Bitmap btm)
		{
			btm = Escala_Grises(btm);
			BitmapData bmpdata = btm.LockBits(new Rectangle(0, 0, btm.Width, btm.Height),
												ImageLockMode.ReadWrite, btm.PixelFormat);
			int numbytes = bmpdata.Stride * btm.Height;
			byte[] bytedata = new byte[numbytes];
			int umbral = 60;
			IntPtr arregloImagen = bmpdata.Scan0;
			Marshal.Copy(arregloImagen, bytedata, 0, numbytes);
			byte[] copiaDatos = new byte[numbytes];
			Array.Copy(bytedata, copiaDatos, numbytes);
			int[,] sobelX = { { -1, 0, 1 }, { -1, 0, 1 }, { -1, 0, 1 } };
			int[,] sobelY = { { -1, -1, -1 }, { 0, 0, 0 }, { 1, 1, 1 } };
			int width = btm.Width;
			int height = btm.Height;
			int stride = bmpdata.Stride;
			Bitmap bordes = new Bitmap(width, height);
			using (Graphics g = Graphics.FromImage(bordes))
			{
				g.Clear(Color.Black);
				for (int y = 1; y < height - 1; y++)
				{
					for (int x = 1; x < width - 1; x++)
					{
						int gx = 0, gy = 0;
						int pos = y * stride + x * 4;
						for (int ky = -1; ky <= 1; ky++)
						{
							for (int kx = -1; kx <= 1; kx++)
							{
								int offset = (y + ky) * stride + (x + kx) * 4;
								byte pixelValue = copiaDatos[offset];

								gx += sobelX[ky + 1, kx + 1] * pixelValue;
								gy += sobelY[ky + 1, kx + 1] * pixelValue;
							}
						}
						int magnitude = (int)Math.Sqrt(gx * gx + gy * gy);
						if (magnitude > umbral)
						{
							using (Brush brush = new SolidBrush(Color.White))
							{
								g.FillRectangle(brush, x, y, 1, 1);
							}
						}
					}
				}
			}
			Marshal.Copy(bytedata, 0, arregloImagen, numbytes);
			btm.UnlockBits(bmpdata);

			return bordes;
		}
		public static Bitmap Etiquetado(Bitmap btm)
		{
			btm = Binarizar(btm);

			int[,] matrizBinaria = new int[btm.Height, btm.Width];
			BitmapData bmpData = btm.LockBits(new Rectangle(0, 0, btm.Width, btm.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

			try
			{
				int bytes = Math.Abs(bmpData.Stride) * btm.Height;
				byte[] rgbValues = new byte[bytes];
				Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

				for (int y = 0; y < btm.Height; y++)
				{
					for (int x = 0; x < btm.Width; x++)
					{
						int index = y * bmpData.Stride + x * 4;
						matrizBinaria[y, x] = rgbValues[index] > 128 ? 1 : 0;
					}
				}
			}
			finally
			{
				btm.UnlockBits(bmpData);
			}

			int[,] labeledMatrix = LabelComponents(matrizBinaria);

			Bitmap labeledImage = new Bitmap(btm.Width, btm.Height);
			Random rand = new Random();
			Dictionary<int, Color> labelColors = new Dictionary<int, Color>();

			for (int y = 0; y < labeledMatrix.GetLength(0); y++)
			{
				for (int x = 0; x < labeledMatrix.GetLength(1); x++)
				{
					int label = labeledMatrix[y, x];
					if (label > 0)
					{
						if (!labelColors.ContainsKey(label))
						{
							labelColors[label] = Color.FromArgb(
								rand.Next(50, 255),
								rand.Next(50, 255),
								rand.Next(50, 255));
						}
						labeledImage.SetPixel(x, y, labelColors[label]);
					}
					else
					{
						labeledImage.SetPixel(x, y, Color.Black);
					}
				}
			}

			return labeledImage;
		}
		public static List<ResultadoMomentosHu> CalcularMomentosHuPorObjeto(Bitmap imagen)
		{
			imagen = Binarizar(imagen);
			int[,] matrizBinaria = ConvertirAMatrizBinaria(imagen);
			int[,] etiquetas = LabelComponents(matrizBinaria);

			int maxLabel = 0;
			for (int y = 0; y < etiquetas.GetLength(0); y++)
			{
				for (int x = 0; x < etiquetas.GetLength(1); x++)
				{
					if (etiquetas[y, x] > maxLabel)
						maxLabel = etiquetas[y, x];
				}
			}

			var resultados = new List<ResultadoMomentosHu>();

			for (int label = 1; label <= maxLabel; label++)
			{
				int[,] objetoBinario = new int[matrizBinaria.GetLength(0), matrizBinaria.GetLength(1)];
				for (int y = 0; y < objetoBinario.GetLength(0); y++)
				{
					for (int x = 0; x < objetoBinario.GetLength(1); x++)
					{
						objetoBinario[y, x] = etiquetas[y, x] == label ? 1 : 0;
					}
				}

				double m00, m10, m01;
				CalcularMomentosGeometricos(objetoBinario, out m00, out m10, out m01);

				if (m00 == 0) continue;

				double xCentro = m10 / m00;
				double yCentro = m01 / m00;

				double mu20, mu11, mu02, mu30, mu21, mu12, mu03;
				CalcularMomentosCentrales(objetoBinario, xCentro, yCentro,
										out mu20, out mu11, out mu02,
										out mu30, out mu21, out mu12, out mu03);

				double[] huMoments = new double[7];
				CalcularMomentosHu(mu20, mu11, mu02, mu30, mu21, mu12, mu03, m00, huMoments);

				resultados.Add(new ResultadoMomentosHu
				{
					Moments = huMoments,
					Center = new PointF((float)xCentro, (float)yCentro)
				});
			}

			return resultados;
		}
		private static int[,] ConvertirAMatrizBinaria(Bitmap imagen)
		{
			int[,] matrizBinaria = new int[imagen.Height, imagen.Width];
			BitmapData bmpData = imagen.LockBits(new Rectangle(0, 0, imagen.Width, imagen.Height),
											 ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

			try
			{
				int bytes = Math.Abs(bmpData.Stride) * imagen.Height;
				byte[] rgbValues = new byte[bytes];
				Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

				for (int y = 0; y < imagen.Height; y++)
				{
					for (int x = 0; x < imagen.Width; x++)
					{
						int index = y * bmpData.Stride + x * 4;
						matrizBinaria[y, x] = rgbValues[index] > 128 ? 1 : 0;
					}
				}
			}
			finally
			{
				imagen.UnlockBits(bmpData);
			}

			return matrizBinaria;
		}
		private static void CalcularMomentosGeometricos(int[,] matrizBinaria, out double m00, out double m10, out double m01)
		{
			m00 = 0; m10 = 0; m01 = 0;
			int height = matrizBinaria.GetLength(0);
			int width = matrizBinaria.GetLength(1);

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					if (matrizBinaria[y, x] == 1)
					{
						m00 += 1;
						m10 += x;
						m01 += y;
					}
				}
			}
		}
		private static void CalcularMomentosCentrales(int[,] matrizBinaria, double xCentro, double yCentro, out double mu20, out double mu11, out double mu02, out double mu30, out double mu21, out double mu12, out double mu03)
		{
			mu20 = 0; mu11 = 0; mu02 = 0;
			mu30 = 0; mu21 = 0; mu12 = 0; mu03 = 0;
			int height = matrizBinaria.GetLength(0);
			int width = matrizBinaria.GetLength(1);

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					if (matrizBinaria[y, x] == 1)
					{
						double xDiff = x - xCentro;
						double yDiff = y - yCentro;

						mu20 += xDiff * xDiff;
						mu11 += xDiff * yDiff;
						mu02 += yDiff * yDiff;

						mu30 += xDiff * xDiff * xDiff;
						mu21 += xDiff * xDiff * yDiff;
						mu12 += xDiff * yDiff * yDiff;
						mu03 += yDiff * yDiff * yDiff;
					}
				}
			}
		}
		private static void CalcularMomentosHu(double mu20, double mu11, double mu02, double mu30, double mu21, double mu12, double mu03, double m00, double[] huMoments)
		{
			double n20 = mu20 / Math.Pow(m00, 2);
			double n11 = mu11 / Math.Pow(m00, 2);
			double n02 = mu02 / Math.Pow(m00, 2);
			double n30 = mu30 / Math.Pow(m00, 2.5);
			double n21 = mu21 / Math.Pow(m00, 2.5);
			double n12 = mu12 / Math.Pow(m00, 2.5);
			double n03 = mu03 / Math.Pow(m00, 2.5);

			huMoments[0] = n20 + n02;
			huMoments[1] = Math.Pow((n20 - n02), 2) + 4 * Math.Pow(n11, 2);
			huMoments[2] = Math.Pow((n30 - 3 * n12), 2) + Math.Pow((3 * n21 - n03), 2);
			huMoments[3] = Math.Pow((n30 + n12), 2) + Math.Pow((n21 + n03), 2);
			huMoments[4] = (n30 - 3 * n12) * (n30 + n12) * (Math.Pow((n30 + n12), 2) - 3 * Math.Pow((n21 + n03), 2)) +
						  (3 * n21 - n03) * (n21 + n03) * (3 * Math.Pow((n30 + n12), 2) - Math.Pow((n21 + n03), 2));
			huMoments[5] = (n20 - n02) * (Math.Pow((n30 + n12), 2) - Math.Pow((n21 + n03), 2)) +
						  4 * n11 * (n30 + n12) * (n21 + n03);
			huMoments[6] = (3 * n21 - n03) * (n30 + n12) * (Math.Pow((n30 + n12), 2) - 3 * Math.Pow((n21 + n03), 2)) -
						  (n30 - 3 * n12) * (n21 + n03) * (3 * Math.Pow((n30 + n12), 2) - Math.Pow((n21 + n03), 2));

			for (int i = 0; i < huMoments.Length; i++)
			{
				if (Math.Abs(huMoments[i]) < 1e-100)
					huMoments[i] = 0;
				else
					huMoments[i] = -Math.Sign(huMoments[i]) * Math.Log10(Math.Abs(huMoments[i]));
			}
		}
		private static int[,] LabelComponents(int[,] binaryImage)
		{
			int width = binaryImage.GetLength(1);
			int height = binaryImage.GetLength(0);
			int[,] labels = new int[height, width];
			Dictionary<int, int> equivalences = new Dictionary<int, int>();
			int currentLabel = 1;

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					if (binaryImage[y, x] == 1)
					{
						List<int> neighborLabels = GetNeighborLabels(labels, y, x);

						if (neighborLabels.Count == 0)
						{
							labels[y, x] = currentLabel;
							currentLabel++;
						}
						else
						{
							int minLabel = GetMinimumLabel(neighborLabels);
							labels[y, x] = minLabel;

							foreach (int label in neighborLabels)
							{
								if (label != minLabel)
								{
									equivalences[label] = minLabel;
								}
							}
						}
					}
				}
			}

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					if (labels[y, x] != 0)
					{
						labels[y, x] = FindRootLabel(equivalences, labels[y, x]);
					}
				}
			}

			Dictionary<int, int> relabelingMap = CreateRelabelingMap(labels);

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					if (labels[y, x] != 0)
					{
						labels[y, x] = relabelingMap[labels[y, x]];
					}
				}
			}

			return labels;
		}
		private static List<int> GetNeighborLabels(int[,] labels, int y, int x)
		{
			List<int> neighbors = new List<int>();
			int height = labels.GetLength(0);
			int width = labels.GetLength(1);

			if (y > 0 && labels[y - 1, x] != 0)
			{
				neighbors.Add(labels[y - 1, x]);
			}

			if (x > 0 && labels[y, x - 1] != 0)
			{
				neighbors.Add(labels[y, x - 1]);
			}

			if (y > 0 && x > 0 && labels[y - 1, x - 1] != 0)
			{
				neighbors.Add(labels[y - 1, x - 1]);
			}

			if (y > 0 && x < width - 1 && labels[y - 1, x + 1] != 0)
			{
				neighbors.Add(labels[y - 1, x + 1]);
			}

			return neighbors;
		}
		private static int GetMinimumLabel(List<int> labels)
		{
			int min = int.MaxValue;
			foreach (int label in labels)
			{
				if (label < min)
				{
					min = label;
				}
			}
			return min;
		}
		private static int FindRootLabel(Dictionary<int, int> equivalences, int label)
		{
			while (equivalences.ContainsKey(label))
			{
				label = equivalences[label];
			}
			return label;
		}
		private static Dictionary<int, int> CreateRelabelingMap(int[,] labels)
		{
			HashSet<int> uniqueLabels = new HashSet<int>();
			int height = labels.GetLength(0);
			int width = labels.GetLength(1);

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					if (labels[y, x] != 0)
					{
						uniqueLabels.Add(labels[y, x]);
					}
				}
			}

			List<int> sortedLabels = new List<int>(uniqueLabels);
			sortedLabels.Sort();

			Dictionary<int, int> relabelingMap = new Dictionary<int, int>();
			int newLabel = 1;

			foreach (int label in sortedLabels)
			{
				relabelingMap[label] = newLabel;
				newLabel++;
			}

			return relabelingMap;
		}
        public static Bitmap Erosionar(Bitmap btm, int tamanoKernel)
        {
            Bitmap btmErosionado = new Bitmap(btm.Width, btm.Height, btm.PixelFormat);

            BitmapData bmpdataOriginal = btm.LockBits(new Rectangle(0, 0, btm.Width, btm.Height), ImageLockMode.ReadWrite, btm.PixelFormat);

            int stride = bmpdataOriginal.Stride;
            byte[] bytedata = new byte[stride * btm.Height];
            Marshal.Copy(bmpdataOriginal.Scan0, bytedata, 0, stride * btm.Height);

            BitmapData bmpdataErosionado = btmErosionado.LockBits(new Rectangle(0, 0, btmErosionado.Width, btmErosionado.Height), ImageLockMode.WriteOnly, btmErosionado.PixelFormat);
            byte[] outputData = new byte[stride * btm.Height];

            int bytesPorPixel = Image.GetPixelFormatSize(btm.PixelFormat) / 8;

            for (int y = 0; y < btm.Height; y++)
            {
                for (int x = 0; x < btm.Width; x++)
                {
                    bool isObject = true;

                    // Recorre el vecindario definido por el kernel
                    for (int ky = -tamanoKernel / 2; ky <= tamanoKernel / 2; ky++)
                    {
                        for (int kx = -tamanoKernel / 2; kx <= tamanoKernel / 2; kx++)
                        {
                            int ny = y + ky;
                            int nx = x + kx;

                            if (ny >= 0 && ny < btm.Height && nx >= 0 && nx < btm.Width)
                            {
                                int indiceVecinos = (ny * stride) + (nx * bytesPorPixel);

                                if (bytedata[indiceVecinos] == 0)
                                {
                                    isObject = false;
                                    break;
                                }
                            }
                            else
                            {
                                isObject = false;
                                break;
                            }
                        }
                        if (!isObject) break;
                    }

                    int indiceActual = (y * stride) + (x * bytesPorPixel);
                    if (isObject)
                    {
                        outputData[indiceActual] = outputData[indiceActual + 1] = outputData[indiceActual + 2] = 255;
                    }
                    else
                    {
                        outputData[indiceActual] = outputData[indiceActual + 1] = outputData[indiceActual + 2] = 0;
                    }
                    if (bytesPorPixel == 4)
                    {
                        outputData[indiceActual + 3] = bytedata[indiceActual + 3];
                    }
                }
            }

            Marshal.Copy(outputData, 0, bmpdataErosionado.Scan0, stride * btm.Height);

            btm.UnlockBits(bmpdataOriginal);
            btmErosionado.UnlockBits(bmpdataErosionado);

            return btmErosionado;
        }
        public static Bitmap Dilatar(Bitmap btm, int tamanoKernel)
        {
            Bitmap btmDilatado = new Bitmap(btm.Width, btm.Height, btm.PixelFormat);

            BitmapData bmpdataOriginal = btm.LockBits(new Rectangle(0, 0, btm.Width, btm.Height), ImageLockMode.ReadWrite, btm.PixelFormat);

            int stride = bmpdataOriginal.Stride;
            byte[] bytedata = new byte[stride * btm.Height];
            Marshal.Copy(bmpdataOriginal.Scan0, bytedata, 0, stride * btm.Height);

            BitmapData bmpdataDilatado = btmDilatado.LockBits(new Rectangle(0, 0, btmDilatado.Width, btmDilatado.Height), ImageLockMode.WriteOnly, btmDilatado.PixelFormat);
            byte[] outputData = new byte[stride * btm.Height];

            Array.Copy(bytedata, outputData, bytedata.Length);

            int bytesPorPixel = Image.GetPixelFormatSize(btm.PixelFormat) / 8;

            for (int y = 0; y < btm.Height; y++)
            {
                for (int x = 0; x < btm.Width; x++)
                {

                    bool isObject = false;
                    for (int ky = -tamanoKernel / 2; ky <= tamanoKernel / 2; ky++)
                    {
                        for (int kx = -tamanoKernel / 2; kx <= tamanoKernel / 2; kx++)
                        {
                            int ny = y + ky;
                            int nx = x + kx;

                            if (ny >= 0 && ny < btm.Height && nx >= 0 && nx < btm.Width)
                            {
                                int indiceVecinos = (ny * stride) + (nx * bytesPorPixel);
                                if (bytedata[indiceVecinos] == 255)
                                {
                                    isObject = true;
                                    break;
                                }
                            }
                        }
                        if (isObject) break;
                    }

                    int indiceActual = (y * stride) + (x * bytesPorPixel);
                    if (isObject)
                    {
                        outputData[indiceActual] = outputData[indiceActual + 1] = outputData[indiceActual + 2] = 255;
                    }
                }
            }

            Marshal.Copy(outputData, 0, bmpdataDilatado.Scan0, stride * btm.Height);

            btm.UnlockBits(bmpdataOriginal);
            btmDilatado.UnlockBits(bmpdataDilatado);
            return btmDilatado;
        }
        public static Bitmap Cierre(Bitmap btm, int tamanoKernel)
        {
            Bitmap imagenDilatada = Dilatar(btm, tamanoKernel);
            Bitmap imagenCerrada = Erosionar(imagenDilatada, tamanoKernel);
            return imagenCerrada;
        }
        public static byte CalcularUmbralOtsu(byte[] datosImagen, int ancho, int alto, int stride)
		{
			int[] histograma = new int[256];

			for (int y = 0; y < alto; y++)
			{
				for (int x = 0; x < ancho; x++)
				{
					int index = y * stride + x * 4;
					byte intensidad = datosImagen[index];
					histograma[intensidad]++;
				}
			}

			int total = ancho * alto;
			float sum = 0;
			for (int t = 0; t < 256; t++) sum += t * histograma[t];

			float sumB = 0;
			int wB = 0;
			int wF = 0;
			float varMax = 0;
			byte threshold = 0;

			for (int t = 0; t < 256; t++)
			{
				wB += histograma[t];
				if (wB == 0) continue;

				wF = total - wB;
				if (wF == 0) break;

				sumB += (float)(t * histograma[t]);

				float mB = sumB / wB;
				float mF = (sum - sumB) / wF;

				float varBetween = (float)wB * (float)wF * (mB - mF) * (mB - mF);

				if (varBetween > varMax)
				{
					varMax = varBetween;
					threshold = (byte)t;
				}
			}

			return threshold;
		}
	}
}