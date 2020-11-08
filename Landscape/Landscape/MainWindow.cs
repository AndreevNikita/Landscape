using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;

namespace Landscape
{
    public partial class MainWindow : Form
    {
        const float scaleXZ = 10.0f; 
        const float sensivity = 0.005f; //Чуствительность мышки 
        const float speed = 10.0f; //Скорость перемещения

        const float lightSpeed = 0.07f;
        float lightYaw = 0.0f;

        //Состояние камеры
        float camYaw = 0.0f; //Вращение
        float camPitch = 0.0f; //Наклон
        Vector4 camDirection = new Vector4(0, 0, -1, 1); //Направление камеры
        Vector3 camPosition = new Vector3(0, 100, 70); //Позиция в пространстве

        bool loaded = false; //Станет true при успешной загрузке
        Vector3 lightPosition = new Vector3(100, 100, 100);
        Vector4 lightDirection = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);

        //Вспомогательные структуры хранения данных
        public struct Vertex {
            public float X, Y, Z;
            public Vertex(float x, float y, float z) {
                X = x; Y = y; Z = z;
            }

            public Vertex(Vector3 vec) {
                X = vec.X;
                Y = vec.Y;
                Z = vec.Z;
            }

            public const int Stride = 12;

            public Vertex subtract(Vertex v) {
                return new Vertex(X - v.X, Y - v.Y, Z - v.Z);
            }

            public Vector3 tov3() {
                return new Vector3(X, Y, Z);
            } 
        }

        public struct Vertex2 {
            float X, Y;
            public Vertex2(float x, float y) {
                X = x;
                Y = y;
            }

            public const int Stride = 8;

            public override string ToString()
            {
                return X + "; " + Y;
            }
        }

        //Вершины, порядок отрисовки ландшафта
        Vertex[] Vertices;
        Vertex2[] TexCoords;
        Vertex[] Normals;
        private int[] Indices;
        private int ID_VBO;
        private int ID_TBO;
        private int ID_NBO;
        private int ID_EBO;

        int texture;

        public MainWindow() {
            InitializeComponent();
        }

        private void LandscapeWindow_Load(object sender, EventArgs e)
        {
            Timer drawTimer = new Timer();
            drawTimer.Tick += drawTick;
            drawTimer.Interval = 10;
            drawTimer.Start();

            Timer keyboardTimer = new Timer();
            keyboardTimer.Tick += keyboardTick;
            keyboardTimer.Interval = 10;
            keyboardTimer.Start();

            Timer mouseTimer = new Timer();
            mouseTimer.Tick += MouseTimer_Tick;
            mouseTimer.Interval = 10;
            mouseTimer.Start();
        }

        Point lastPos = new Point();
        bool lastKeyDown = false;

        //Метод, следящий за мышкой. Каждый раз считает разницу позиций курсора и высчитывает углы поворота камеры (camYaw и camPitch)
        private void MouseTimer_Tick(object sender, EventArgs e)
        {
            //Получаем объекты мыши и клавиатуры
            MouseState ms = Mouse.GetState();
            KeyboardState kbs = Keyboard.GetState();
            if (kbs.IsKeyDown(Key.Space)) { //Если нажат пробел, то ничего не делаем (в том числе, не возвращаем курсор в центр окна)
                lastKeyDown = true;
                return;
            }

            if (lastKeyDown) {
                lastPos = new Point(ms.X, ms.Y); //Устанавливаем прошлую позицию мышки как только отпустили пробел
                lastKeyDown = false;
            }

            //Находим изменение в координатах
            int dx = ms.X - lastPos.X;
            int dy = ms.Y - lastPos.Y;
            //Добавляем к уже имеющимся углам
            float dYaw = dx * sensivity;
            float dPitch = dy * sensivity;
            camYaw += dYaw;
            camPitch += dPitch;
            if (camPitch > Math.PI / 2 - 0.001f)
                camPitch = (float)Math.PI / 2 - 0.001f;
            if (camPitch < -Math.PI / 2 - 0.001f)
                camPitch = (float)-Math.PI / 2 - 0.001f;

            Vector4 direction = new Vector4(0.0f, 0.0f, -1.0f, 1.0f);
            direction = mul(Matrix4.RotateX(camPitch), direction);
            direction = mul(Matrix4.RotateY(camYaw), direction);
            camDirection = direction;

            Point glAreaPosition = new Point();
            glAreaPosition = glControl.PointToScreen(new Point(0, 0));

            Mouse.SetPosition(glAreaPosition.X + glControl.Width / 2, glAreaPosition.Y + glControl.Height / 2); //Смещаем курсор обратно в центр окна

            lastPos = new Point(ms.X, ms.Y);
        }

        //Метод, умножающий матрицу на вектор (не нашёл такого в OpenTK)
        private Vector4 mul(Matrix4 mat, Vector4 vec)
        {
            return new Vector4(
                mat.Row0[0] * vec.X + mat.Row0[1] * vec.Y + mat.Row0[2] * vec.Z + mat.Row0[3] * vec.W,
                mat.Row1[0] * vec.X + mat.Row1[1] * vec.Y + mat.Row1[2] * vec.Z + mat.Row1[3] * vec.W,
                mat.Row2[0] * vec.X + mat.Row2[1] * vec.Y + mat.Row2[2] * vec.Z + mat.Row2[3] * vec.W, 
                mat.Row3[0] * vec.X + mat.Row3[1] * vec.Y + mat.Row3[2] * vec.Z + mat.Row3[3] * vec.W
            );
        }

        //Загрузка текстуры
        static int loadTexture(string filename)
        {
            if (String.IsNullOrEmpty(filename))
                throw new ArgumentException(filename);

            int id = GL.GenTexture(); //Создаём новую текстуру в OpenGL
            GL.BindTexture(TextureTarget.Texture2D, id); //Говорим, что сейчас будем работать с ней

            Bitmap bmp = new Bitmap(filename); //Загружаем изображение текстуры
            //Получаем пиксели изображения
            BitmapData bmp_data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);


            //Передаём пиксели загруженного изображеняия изображению в OpenGL
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp_data.Width, bmp_data.Height, 0,
                OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, bmp_data.Scan0);

            bmp.UnlockBits(bmp_data);

            //Параметры фильтрации текстур (при масштабировании)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            return id;
        }

        private void drawTick(object sender, EventArgs e) //Метод исполняется каждые 10 мс и перерисовывает изображение
        {
            glControl.Refresh();
        }

        //Раз в 10 миллисекунд кнопки клавиатуры проверяются на нажатие. Если кнопка нажата, камера сдвигается в соответствующем направлении
        private void keyboardTick(object sender, EventArgs e) {
            Vector2 dir = new Vector2(camDirection.X, camDirection.Z);
            dir.Normalize();
            
            KeyboardState kbs = Keyboard.GetState();
            if (kbs.IsKeyDown(Key.W))
                camPosition += new Vector3(camDirection.X, camDirection.Y, camDirection.Z);
            if (kbs.IsKeyDown(Key.S))
                camPosition -= new Vector3(camDirection.X, camDirection.Y, camDirection.Z);
            if (kbs.IsKeyDown(Key.A))
                camPosition -= new Vector3(-dir.Y, 0, dir.X);
            if (kbs.IsKeyDown(Key.D))
                camPosition += new Vector3(-dir.Y, 0, dir.X);

            //Увеличиваем и уменьшаем угол освещения при нажатии клавиш E и Q
            if (kbs.IsKeyDown(Key.Q)) {
                lightYaw += lightSpeed;
            }
            if (kbs.IsKeyDown(Key.E )) {
                lightYaw -= lightSpeed;
            }

            Vector4 direction = new Vector4(0.0f, 0.0f, -4f, 1.0f);
            direction = mul(Matrix4.RotateY(lightYaw), direction);
            lightDirection = direction;
            Console.WriteLine(direction);
        }
        int mapList;

        //Здесь мы загружаем изображение
        private void glControl_Load(object sender, EventArgs e) {
            loaded = true;
            Bitmap heightMapImage = new Bitmap(Image.FromFile("Ландшафт.jpg"));
            int vIndex = 0; //Вспомогательная переменная, которая поможет при записи двумерных данных последовательно в одномерный массив
            //Количество вершин
            int verticesQuantity = (heightMapImage.Width - 1) * (heightMapImage.Height - 1) * 6 + heightMapImage.Width * 6 * 2 + heightMapImage.Height * 6 * 2;
            Vertices = new Vertex[verticesQuantity];
            float heightMul = 0.25f; //Множитель высоты
            //Загружаем нашу карту высот
            for (int x = 0; x < heightMapImage.Width - 1; x++)
                for (int z = 0; z < heightMapImage.Height - 1; z++) {
                    
                    Vertices[vIndex++] = new Vertex(x, (float)heightMapImage.GetPixel(x, z).R * heightMul, z);
                    Vertices[vIndex++] = new Vertex(x + 1, (float)heightMapImage.GetPixel(x + 1, z + 1).R * heightMul, z + 1);
                    Vertices[vIndex++] = new Vertex(x + 1, (float)heightMapImage.GetPixel(x + 1, z).R * heightMul, z);

                    Vertices[vIndex++] = new Vertex(x, (float)heightMapImage.GetPixel(x, z).R * heightMul, z);
                    Vertices[vIndex++] = new Vertex(x, (float)heightMapImage.GetPixel(x, z + 1).R * heightMul, z + 1);
                    Vertices[vIndex++] = new Vertex(x + 1, (float)heightMapImage.GetPixel(x + 1, z + 1).R * heightMul, z + 1);
                }

            //Этот блок можно убрать в пользу производительности
            //Здесь генерируются стенки карты (для красоты)
            for (int x = 0; x < heightMapImage.Width - 1; x++) {
                Vertices[vIndex++] = new Vertex(x, (float)heightMapImage.GetPixel(x, 0).R * heightMul, 0);
                Vertices[vIndex++] = new Vertex(x + 1, (float)heightMapImage.GetPixel(x + 1, 0).R * heightMul, 0);
                Vertices[vIndex++] = new Vertex(x + 1, 0, 0);
                

                Vertices[vIndex++] = new Vertex(x, (float)heightMapImage.GetPixel(x, 0).R * heightMul, 0);
                Vertices[vIndex++] = new Vertex(x + 1, 0, 0);
                Vertices[vIndex++] = new Vertex(x, 0, 0);
            }

            for (int x = 0; x < heightMapImage.Width - 1; x++) {
                Vertices[vIndex++] = new Vertex(x, (float)heightMapImage.GetPixel(x, heightMapImage.Height - 1).R * heightMul, heightMapImage.Height - 1);
                Vertices[vIndex++] = new Vertex(x + 1, 0, heightMapImage.Height - 1);
                Vertices[vIndex++] = new Vertex(x + 1, (float)heightMapImage.GetPixel(x + 1, heightMapImage.Height - 1).R * heightMul, heightMapImage.Height - 1);
                

                Vertices[vIndex++] = new Vertex(x, (float)heightMapImage.GetPixel(x, heightMapImage.Height - 1).R * heightMul, heightMapImage.Height - 1);
                Vertices[vIndex++] = new Vertex(x, 0, heightMapImage.Height - 1);
                Vertices[vIndex++] = new Vertex(x + 1, 0, heightMapImage.Height - 1);
            }

            for (int z = 0; z < heightMapImage.Height - 1; z++) {
                Vertices[vIndex++] = new Vertex(0, (float)heightMapImage.GetPixel(0, z).R * heightMul, z);
                Vertices[vIndex++] = new Vertex(0, 0, z + 1);
                Vertices[vIndex++] = new Vertex(0, (float)heightMapImage.GetPixel(0, z + 1).R * heightMul, z + 1);

                Vertices[vIndex++] = new Vertex(0, (float)heightMapImage.GetPixel(0, z).R * heightMul, z);
                Vertices[vIndex++] = new Vertex(0, 0, z);
                Vertices[vIndex++] = new Vertex(0, 0, z + 1);
            }

            for (int z = 0; z < heightMapImage.Height - 1; z++) {
                Vertices[vIndex++] = new Vertex(heightMapImage.Width - 1, (float)heightMapImage.GetPixel(heightMapImage.Width - 1, z).R * heightMul, z);
                Vertices[vIndex++] = new Vertex(heightMapImage.Width - 1, (float)heightMapImage.GetPixel(heightMapImage.Width - 1, z + 1).R * heightMul, z + 1);
                Vertices[vIndex++] = new Vertex(heightMapImage.Width - 1, 0, z + 1);
                

                Vertices[vIndex++] = new Vertex(heightMapImage.Width - 1, (float)heightMapImage.GetPixel(heightMapImage.Width - 1, z).R * heightMul, z);
                Vertices[vIndex++] = new Vertex(heightMapImage.Width - 1, 0, z + 1);
                Vertices[vIndex++] = new Vertex(heightMapImage.Width - 1, 0, z);
            }

            //Получившийся массив вершин записывается в буфер OpenGL
            GL.GenBuffers(1, out ID_VBO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, ID_VBO);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(Vertices.Length * Vertex.Stride), Vertices, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            //Расчитываем нормаль для каждого треугольника ландшафта
            Normals = new Vertex[verticesQuantity];
            for (int index = 0; index < Vertices.Length; index += 3) {
                //По трём точкам находим нормаль к плоскости и записываем её в массив
                Normals[index] = new Vertex(Vector3.Normalize(Vector3.Cross(Vertices[index + 1].tov3() - Vertices[index + 0].tov3(), Vertices[index + 2].tov3() - Vertices[index + 0].tov3())));
                Normals[index + 2] = Normals[index + 1] = Normals[index];
            }

            //Записываем массив нормалей в буфер
            GL.GenBuffers(1, out ID_NBO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, ID_NBO);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(Normals.Length * Vertex.Stride), Normals, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            //Аналогично поступаем с текстурными координатами
            TexCoords = new Vertex2[verticesQuantity];
            vIndex = 0;
            for (int x = 0; x < heightMapImage.Width - 1  ; x++)
                for (int z = 0; z < heightMapImage.Height - 1; z++) {
                    float X_L = (float)(x) / (float)(heightMapImage.Width - 1);
                    float X_M = (float)(x + 1) / (float)(heightMapImage.Width - 1);
                    float Z_L = (float)(z) / (float)(heightMapImage.Height - 1);
                    float Z_M = (float)(z + 1) / (float)(heightMapImage.Height - 1);

                    TexCoords[vIndex++] = new Vertex2(X_L, Z_L);
                    TexCoords[vIndex++] = new Vertex2(X_M, Z_M);
                    TexCoords[vIndex++] = new Vertex2(X_M, Z_L);

                    TexCoords[vIndex++] = new Vertex2(X_L, Z_L);
                    TexCoords[vIndex++] = new Vertex2(X_L, Z_M);
                    TexCoords[vIndex++] = new Vertex2(X_M, Z_M);

                }

            //Пишем их в буфер
            GL.GenBuffers(1, out ID_TBO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, ID_TBO);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(TexCoords.Length * Vertex2.Stride), TexCoords, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            //Индексы достаточно указать 1, 2, 3, 4 ... Т.к. вершины уже расположены в массиве по порядку
            Indices = new int[Vertices.Length];
            for (int index = 0; index < Indices.Length; index++)
                Indices[index] = index;
            
            GL.GenBuffers(1, out ID_EBO);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ID_EBO);
            GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(Indices.Length * sizeof(int)), Indices, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

            //Загружаем изображение и получаем его Id в OpenGL
            texture = loadTexture("Текстура.jpg");
            GL.ShadeModel(ShadingModel.Smooth);
        }

        //Этот метод выполняется при перерисоовывании glControl на окне
        private void glControl_Paint(object sender, PaintEventArgs e)
        {
            if (!loaded)
                return;

            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); //Очищаем экран и буфер глубины
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Diffuse, new float[] { 1.0f, 1.0f, 1.0f, 1.0f });
            GL.Enable(EnableCap.DepthTest); //Включаем тест глубины, чтобы соблюдался порядок по дальности
            GL.Enable(EnableCap.Lighting); //Говорим, что у нас будет освещение
            GL.Enable(EnableCap.Texture2D); //Говорим, что будем накладывать текстуры
            //Говорим, какие источники света будут задействованы
            GL.Enable(EnableCap.Light0);
            GL.Enable(EnableCap.Light1);
            GL.Light(LightName.Light0, LightParameter.SpotCutoff, 180.0f);
            GL.Light(LightName.Light1, LightParameter.SpotCutoff, 180.0f);

            //Обновляем матрицу вида
            GL.MatrixMode(MatrixMode.Modelview);
            Matrix4 viewMatrix = Matrix4.LookAt(camPosition.X, camPosition.Y, camPosition.Z, camPosition.X + camDirection.X, camPosition.Y + camDirection.Y, camPosition.Z + camDirection.Z, 0, 1, 0);
            GL.LoadMatrix(ref viewMatrix);

            GL.PushMatrix();
            GL.Scale(scaleXZ, 0.0f, scaleXZ);
            //GL.Light();
            
            GL.Light(LightName.Light1, LightParameter.Ambient, new float[] { 1.0f, 1.0f, 1.0f, 1.0f });
            GL.Light(LightName.Light0, LightParameter.Position, new float[] { lightDirection.X, -10.0f, lightDirection.Z, 0.0f});

            GL.PopMatrix();
            //Рисуем ландшафт
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.BindBuffer(BufferTarget.ArrayBuffer, ID_VBO);
            GL.VertexPointer(3, VertexPointerType.Float, Vertex.Stride, 0); //Говорим компьютеру использовать буфер вершин

            GL.EnableClientState(ArrayCap.NormalArray);
            GL.BindBuffer(BufferTarget.ArrayBuffer, ID_NBO);
            GL.NormalPointer(NormalPointerType.Float, Vertex.Stride, 0); //Говорим компьютеру использовать буфер нормалей

            GL.EnableClientState(ArrayCap.TextureCoordArray);
            GL.BindBuffer(BufferTarget.ArrayBuffer, ID_TBO);
            GL.TexCoordPointer(2, TexCoordPointerType.Float, Vertex2.Stride, 0); //Говорим компьютеру использовать буфер координат текстуры

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ID_EBO);
            GL.DrawElements(BeginMode.Triangles, Indices.Length, DrawElementsType.UnsignedInt, 0); //Рисуем элементы в порядке, заданном Indices

            //Говорим компьютеру, что наши буферы дальше использовать не надо
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

            //glControl выводит всё что мы нарисовали ранее
            glControl.SwapBuffers();
            //loaded = false;
        }

        //Метод, вызываемый при изменении размера окна
        private void glControl_Resize(object sender, EventArgs e) {
            GL.Viewport(0, 0, glControl.Width, glControl.Height); //Устанавливаем новые координаты, где нужно отрисовывать картинку

            //Матрица проекции тоже меняется
            Matrix4 p = Matrix4.CreatePerspectiveFieldOfView((float)(80 * Math.PI / 180), (float)glControl.Width / (float)glControl.Height, 0.1f, 6000);
            //Устанавливаем матрицу проекции, полученную выше
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref p);
        }

    }
}
