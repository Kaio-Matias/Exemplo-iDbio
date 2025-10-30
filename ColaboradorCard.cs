using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CaptureExample
{
    // DTO que esperamos da API (AGORA CORRESPONDE AO SEU DTO)
    public class ColaboradorDTO
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public string CartaoPonto { get; set; }
        public string Funcao { get; set; }
        public string Departamento { get; set; }
        public bool Ativo { get; set; }
        public byte[] Foto { get; set; }
        public int FuncaoId { get; set; }
        public int DepartamentoId { get; set; }
        public bool AcessoCafeDaManha { get; set; } = false;
        public bool AcessoAlmoco { get; set; } = false;
        public bool AcessoJanta { get; set; } = false;
        public bool AcessoCeia { get; set; } = false;
    }

    public partial class ColaboradorCard : UserControl
    {
        private ColaboradorDTO _colaborador;
        private bool _selecionado = false;

        public ColaboradorCard(ColaboradorDTO colaborador)
        {
            InitializeComponent();
            _colaborador = colaborador;
            lblNome.Text = colaborador.Nome;

            // Carrega a foto
            if (colaborador.Foto != null && colaborador.Foto.Length > 0)
            {
                try
                {
                    using (var ms = new MemoryStream(colaborador.Foto))
                    {
                        picFoto.Image = Image.FromStream(ms);
                    }
                }
                catch (Exception)
                {
                    // Se a foto estiver corrompida, usa a imagem padrão
                    picFoto.Image = picFoto.ErrorImage;
                }
            }
            else
            {
                // Se não houver foto, usa a imagem padrão
                picFoto.Image = picFoto.ErrorImage;
            }

            // Adiciona o handler de clique para todos os controles
            this.Click += Card_Click;
            lblNome.Click += Card_Click;
            picFoto.Click += Card_Click;
        }

        public ColaboradorDTO Colaborador => _colaborador;

        // Evento que será disparado quando este card for clicado
        public event EventHandler CardSelecionado;

        private void Card_Click(object sender, EventArgs e)
        {
            // Dispara o evento para o formulário principal
            CardSelecionado?.Invoke(this, e);
        }

        public bool Selecionado
        {
            get => _selecionado;
            set
            {
                _selecionado = value;
                // Altera a cor de fundo para indicar seleção
                this.BackColor = _selecionado ? Color.LightSkyBlue : Color.White;
            }
        }
    }
}