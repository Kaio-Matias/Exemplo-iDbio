using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows.Forms;
using ControliD;
// --- NOVOS USINGS ---
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Text;
using System.Collections.Generic;
using System.Linq; // Para o filtro

namespace CaptureExample
{
    public partial class Example : Form
    {
        #region initilization

        private CIDBio idbio = new CIDBio();

        // --- ADIÇÕES PARA API ---

        // HttpClient agora é por instância, para conter o token
        private HttpClient httpClient = new HttpClient();
        // ATENÇÃO: Confirme se esta é a URL correta da sua API
        private const string ApiBaseUrl = "http://localhost:5114";
        private string jwtToken = null; // Armazena o token de login
        private ColaboradorDTO colaboradorSelecionado = null; // Armazena quem foi clicado
        private List<ColaboradorDTO> listaColaboradoresCache = new List<ColaboradorDTO>(); // Cache para filtro

        // Classe DTO para enviar o login
        public class LoginRequest
        {
            public string Username { get; set; }
            public string Password { get; set; }
        }

        // Classe DTO para receber o token
        public class LoginResponse
        {
            public string Token { get; set; }
            // Adicione outras propriedades se a API retornar (ex: Usuario, Role)
        }

        // Classe DTO para enviar a biometria para a API
        public class CadastroBiometriaRequest
        {
            public int ColaboradorId { get; set; }
            public string BiometriaTemplateBase64 { get; set; }
        }
        // --- FIM DAS ADIÇÕES ---


        public Example()
        {
            InitializeComponent();
            // O token será adicionado no httpClient *depois* do login
        }

        private void Example_Load(object sender, EventArgs e)
        {
            var ret = CIDBio.Init();
            if (ret == RetCode.SUCCESS)
            {
                captureLog.Text += "Init Successful\r\n";
                captureBtn.Enabled = true;
            }
            else if (ret < RetCode.SUCCESS)
            {
                captureLog.Text += "Init Error: " + CIDBio.GetErrorMessage(ret) + "\r\n";
            }
            else
            {
                captureLog.Text += "Init Warning: " + CIDBio.GetErrorMessage(ret) + "\r\n";
                captureBtn.Enabled = true;
            }

            // Desabilita as abas até o login ser feito
            tabPage1.Enabled = false;
            tabPage2.Enabled = false;
            tabPage3.Enabled = false;
        }

        private void CaptureExample_FormClosed(object sender, FormClosedEventArgs e)
        {
            CIDBio.Terminate();
        }

        private void TabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Se o usuário tentar sair da aba de login sem estar logado, force-o a voltar
            if (tabControl1.SelectedTab != tabPageLogin && jwtToken == null)
            {
                MessageBox.Show("Você precisa fazer login primeiro.", "Acesso Negado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tabControl1.SelectedTab = tabPageLogin;
                return;
            }

            // A lógica original de carregar IDs, etc.
            if (tabControl1.SelectedTab == tabPage2) // tabPage2 (Identification)
            {
                ReloadIDs(); // Recarrega IDs do *dispositivo*
            }
            else if (tabControl1.SelectedTab == tabPage3) // tabPage3 (Configuration)
            {
                LoadAllConfig();
            }
        }

        #endregion

        // --- NOVA REGIÃO DE LOGIN ---
        #region Login

        private async void btnLogin_Click(object sender, EventArgs e)
        {
            var loginRequest = new LoginRequest
            {
                Username = txtUsername.Text,
                Password = txtPassword.Text
            };

            var jsonContent = JsonConvert.SerializeObject(loginRequest);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                lblLoginStatus.Text = "Autenticando...";

                // O endpoint /api/auth/login DEVE estar com [AllowAnonymous] na API
                var response = await httpClient.PostAsync(ApiBaseUrl + "/api/auth/login", httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    lblLoginStatus.Text = "Usuário ou senha inválidos.";
                    lblLoginStatus.ForeColor = Color.Red;
                    return;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var loginResponse = JsonConvert.DeserializeObject<LoginResponse>(jsonResponse);

                if (loginResponse == null || string.IsNullOrEmpty(loginResponse.Token))
                {
                    lblLoginStatus.Text = "Erro ao ler o token da API.";
                    lblLoginStatus.ForeColor = Color.Red;
                    return;
                }

                // --- SUCESSO ---
                this.jwtToken = loginResponse.Token;

                // Adiciona o token no cabeçalho do httpClient para TODAS as futuras requisições
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.jwtToken);

                lblLoginStatus.Text = "Conectado!";
                lblLoginStatus.ForeColor = Color.Green;

                // Habilita as outras abas
                tabPage1.Enabled = true;
                tabPage2.Enabled = true;
                tabPage3.Enabled = true;

                // Move o usuário para a próxima aba
                tabControl1.SelectedTab = tabPage2;
            }
            catch (Exception ex)
            {
                lblLoginStatus.Text = "Erro de conexão com a API.";
                lblLoginStatus.ForeColor = Color.Red;
                MessageBox.Show(ex.Message, "Erro de Conexão", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region capture

        private void EnableButtons(bool enable)
        {
            checkBtn.Enabled = enable;
            captureBtn.Enabled = enable;
            openUpdateFilebtn.Enabled = enable;
        }

        public static Bitmap ImageBufferToBitmap(byte[] imageBuf, uint width, uint height)
        {
            Bitmap img = new Bitmap((int)width, (int)height);
            for (int x = 0; x < img.Width; x++)
            {
                for (int y = 0; y < img.Height; y++)
                {
                    var color = Color.FromArgb(imageBuf[x + img.Width * y], imageBuf[x + img.Width * y], imageBuf[x + img.Width * y]);
                    img.SetPixel(x, y, color);
                }
            }
            return img;
        }

        private void RenderImage(byte[] imageBuf, uint width, uint height)
        {
            var img = ImageBufferToBitmap(imageBuf, width, height);
            fingerImage.Image = img;
            fingerImage.Width = img.Width;
            fingerImage.Height = img.Height;
        }

        private void CheckBtn_Click(object sender, EventArgs e)
        {
            EnableButtons(false);

            var ret = idbio.GetDeviceInfo(out string version, out string serialNumber, out string model);
            if (ret < RetCode.SUCCESS)
            {
                captureLog.Text += "GetDeviceInfo Error: " + CIDBio.GetErrorMessage(ret) + "\r\n";
            }
            else
            {
                SerialTextBox.Text = serialNumber;
                ModelTextBox.Text = model;
                VersionTextBox.Text = version;
            }
            EnableButtons(true);
        }

        struct FingerImage
        {
            public RetCode ret;
            public byte[] imageBuf;
            public uint width;
            public uint height;
        }

        private async void CaptureBtn_Click(object sender, EventArgs e)
        {
            EnableButtons(false);
            captureLog.Text = "Waiting for finger...\r\n";

            var img = await Task.Run(() => {
                return new FingerImage
                {
                    ret = idbio.CaptureImage(out byte[] imageBuf, out uint width, out uint height),
                    imageBuf = imageBuf,
                    width = width,
                    height = height
                };
            });

            if (img.ret < RetCode.SUCCESS)
            {
                captureLog.Text += "Capture Error: " + CIDBio.GetErrorMessage(img.ret) + "\r\n";
                fingerImage.Image = null;
            }
            else
            {
                captureLog.Text += "Capture Success\r\n";
                RenderImage(img.imageBuf, img.width, img.height);
            }

            EnableButtons(true);
        }

        private async void OpenUpdateFilebtn_Click(object sender, EventArgs e)
        {
            EnableButtons(false);
            if (updateFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (MessageBox.Show("Are you sure you want to update iDBio with the file: " + updateFileDialog.FileName,
                                     "Confirm Update",
                                     MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    captureLog.Text += "Updating iDBio...\r\n";
                    var ret = await Task.Run(() => {
                        return idbio.UpdateFirmware(updateFileDialog.FileName);
                    });
                    if (ret < RetCode.SUCCESS)
                    {
                        captureLog.Text += "Error Updating: " + CIDBio.GetErrorMessage(ret) + "\r\n";
                    }
                    else
                    {
                        captureLog.Text += "Update Successful!\r\n";
                    }
                }
            }
            EnableButtons(true);
        }

        private void CaptureLog_TextChanged(object sender, EventArgs e)
        {
            // set the current caret position to the end
            captureLog.SelectionStart = captureLog.Text.Length;
            // scroll it automatically
            captureLog.ScrollToCaret();
        }

        #endregion

        #region identification (Modificado)

        private void EnableIdentifyButtons(bool enable)
        {
            // Botões antigos (cadastro no dispositivo)
            readAllBtn.Enabled = enable;
            enrollBtn.Enabled = enable;
            IdentifyBtn.Enabled = enable;
            deleteAllBtn.Enabled = enable;

            // Novos botões (cadastro na API)
            btnCarregarLista.Enabled = enable;
            btnCadastrarApi.Enabled = enable;
        }

        private void ReloadIDs()
        {
            var ret = idbio.GetTemplateIDs(out long[] ids);
            if (ret < RetCode.SUCCESS)
            {
                IdentifyLog.Text += "Error Reading IDs: " + CIDBio.GetErrorMessage(ret) + "\r\n";
            }
            else
            {
                iDsList.Items.Clear();
                foreach (var id in ids)
                {
                    iDsList.Items.Add(id.ToString());
                }
            }
        }

        private void ReadAllBtn_Click(object sender, EventArgs e)
        {
            ReloadIDs();
        }

        private void DeleteAllBtn_Click(object sender, EventArgs e)
        {
            var ret = idbio.DeleteAllTemplates();
            if (ret < RetCode.SUCCESS)
            {
                IdentifyLog.Text += "Error Deleting IDs: " + CIDBio.GetErrorMessage(ret) + "\r\n";
            }
            else
            {
                IdentifyLog.Text += "IDs Deleted\r\n";
            }
            ReloadIDs();
        }

        private async void EnrollBtn_Click(object sender, EventArgs e)
        {
            EnableIdentifyButtons(false);
            try
            {
                long id = long.Parse(enrollIDTextBox.Text);

                IdentifyLog.Text = "Enrolling... Press your finger 3 times on the device\r\n";
                var ret = await Task.Run(() => {
                    return idbio.CaptureAndEnroll(id);
                });
                if (ret < RetCode.SUCCESS)
                {
                    IdentifyLog.Text += "Error Enrolling: " + CIDBio.GetErrorMessage(ret) + "\r\n";
                }
                else
                {
                    IdentifyLog.Text += "ID " + id + " Enrolled\r\n";
                }
                ReloadIDs();
            }
            catch (Exception ex)
            {
                IdentifyLog.Text += "Invalid ID: " + ex.Message + "\r\n";
            }
            EnableIdentifyButtons(true);
        }

        struct IdentifyRet
        {
            public RetCode ret;
            public long id;
            public int score;
            public int quality;
        }

        private async void IdentifyBtn_Click(object sender, EventArgs e)
        {
            EnableIdentifyButtons(false);

            IdentifyLog.Text = "Identifying...\r\n";
            var identify = await Task.Run(() => {
                return new IdentifyRet
                {
                    ret = idbio.CaptureAndIdentify(out long id, out int score, out int quality),
                    id = id,
                    score = score,
                    quality = quality
                };
            });
            if (identify.ret < RetCode.SUCCESS)
            {
                IdentifyLog.Text += "Error Identifying: " + CIDBio.GetErrorMessage(identify.ret) +
                    " (quality: " + identify.quality + ")\r\n";
                identifyTextBox.Text = "X";
            }
            else
            {
                IdentifyLog.Text += "ID " + identify.id + " Identified (score: " + identify.score +
                    ", quality: " + identify.quality + ")\r\n";
                identifyTextBox.Text = identify.id.ToString();
            }

            EnableIdentifyButtons(true);
        }

        private void IdentifyLog_TextChanged(object sender, EventArgs e)
        {
            // set the current caret position to the end
            IdentifyLog.SelectionStart = IdentifyLog.Text.Length;
            // scroll it automatically
            IdentifyLog.ScrollToCaret();
        }

        // --- MÉTODOS DA NOVA UI (API) ---

        private async void btnCarregarLista_Click(object sender, EventArgs e)
        {
            IdentifyLog.Text = "Buscando colaboradores da API...\r\n";
            EnableIdentifyButtons(false);
            try
            {
                // Esta chamada agora vai funcionar por causa do token
                var response = await httpClient.GetAsync(ApiBaseUrl + "/api/colaboradores");

                if (!response.IsSuccessStatusCode)
                {
                    IdentifyLog.Text += $"Erro ao buscar: {response.ReasonPhrase}\r\n";
                    EnableIdentifyButtons(true);
                    return;
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                // Usamos a ColaboradorDTO (que agora tem Foto)
                listaColaboradoresCache = JsonConvert.DeserializeObject<List<ColaboradorDTO>>(jsonString);

                // Popula a interface
                FiltrarColaboradores();

                IdentifyLog.Text += "Colaboradores carregados!\r\n";
            }
            catch (Exception ex)
            {
                IdentifyLog.Text += $"Falha na requisição: {ex.Message}\r\n";
            }
            EnableIdentifyButtons(true);
        }

        private void Filros_TextChanged(object sender, EventArgs e)
        {
            // Dispara o filtro quando o texto muda
            FiltrarColaboradores();
        }

        private void FiltrarColaboradores()
        {
            flpColaboradores.Controls.Clear();
            this.colaboradorSelecionado = null; // Limpa a seleção

            string filtroNome = txtFiltroNome.Text.ToLower();

            // A flag foi removida, então filtramos apenas por nome
            var listaFiltrada = listaColaboradoresCache.Where(c =>
            {
                // Condição de filtro de nome
                return string.IsNullOrEmpty(filtroNome) || c.Nome.ToLower().Contains(filtroNome);
            });

            foreach (var col in listaFiltrada)
            {
                var card = new ColaboradorCard(col);
                card.CardSelecionado += Card_CardSelecionado; // Adiciona o evento de clique
                flpColaboradores.Controls.Add(card);
            }
        }

        private void Card_CardSelecionado(object sender, EventArgs e)
        {
            var cardClicado = (ColaboradorCard)sender;
            this.colaboradorSelecionado = cardClicado.Colaborador;

            IdentifyLog.Text = $"Colaborador selecionado: {colaboradorSelecionado.Nome}\r\n";

            // Desmarca todos os outros cartões
            foreach (Control c in flpColaboradores.Controls)
            {
                if (c is ColaboradorCard card)
                {
                    card.Selecionado = (card == cardClicado);
                }
            }
        }

        // Estrutura para conter o resultado da Task de captura e merge
        struct CaptureAndMergeResult
        {
            public RetCode ret;
            public string finalTemplate; // O template Base64 mesclado
            public string errorMessage;
        }

        private async void btnCadastrarApi_Click(object sender, EventArgs e)
        {
            if (this.colaboradorSelecionado == null)
            {
                MessageBox.Show("Por favor, selecione um colaborador na lista.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            IdentifyLog.Text += $"Iniciando cadastro para: {colaboradorSelecionado.Nome}...\r\n";
            EnableIdentifyButtons(false);

            // 1. Capturar 3 templates e mesclá-los (lógica da documentação)
            var result = await Task.Run(() => {

                string temp1, temp2, temp3, tempFinal;
                int quality;
                byte[] imageBuf;
                uint width, height;

                // --- CAPTURA 1 ---
                IdentifyLog.Invoke(new Action(() => IdentifyLog.Text += "Por favor, posicione o dedo (1/3)...\r\n"));
                RetCode ret = idbio.CaptureImageAndTemplate(out temp1, out imageBuf, out width, out height, out quality);
                if (ret < RetCode.SUCCESS)
                {
                    return new CaptureAndMergeResult { ret = ret, errorMessage = "Falha na Captura 1: " + CIDBio.GetErrorMessage(ret) };
                }

                // --- CAPTURA 2 ---
                IdentifyLog.Invoke(new Action(() => IdentifyLog.Text += "Posicione o mesmo dedo novamente (2/3)...\r\n"));
                ret = idbio.CaptureImageAndTemplate(out temp2, out imageBuf, out width, out height, out quality);
                if (ret < RetCode.SUCCESS)
                {
                    return new CaptureAndMergeResult { ret = ret, errorMessage = "Falha na Captura 2: " + CIDBio.GetErrorMessage(ret) };
                }

                // --- CAPTURA 3 ---
                IdentifyLog.Invoke(new Action(() => IdentifyLog.Text += "Posicione o dedo mais uma vez (3/3)...\r\n"));
                ret = idbio.CaptureImageAndTemplate(out temp3, out imageBuf, out width, out height, out quality);
                if (ret < RetCode.SUCCESS)
                {
                    return new CaptureAndMergeResult { ret = ret, errorMessage = "Falha na Captura 3: " + CIDBio.GetErrorMessage(ret) };
                }

                // --- MESCLAR (Merge) ---
                IdentifyLog.Invoke(new Action(() => IdentifyLog.Text += "Processando templates...\r\n"));
                ret = idbio.MergeTemplates(temp1, temp2, temp3, out tempFinal);
                if (ret < RetCode.SUCCESS)
                {
                    return new CaptureAndMergeResult { ret = ret, errorMessage = "Falha ao mesclar templates: " + CIDBio.GetErrorMessage(ret) };
                }

                // Sucesso
                return new CaptureAndMergeResult { ret = RetCode.SUCCESS, finalTemplate = tempFinal };
            });

            // Processar o resultado
            if (result.ret < RetCode.SUCCESS)
            {
                IdentifyLog.Text += $"Erro: {result.errorMessage}\r\n";
                EnableIdentifyButtons(true);
                return;
            }

            if (string.IsNullOrEmpty(result.finalTemplate))
            {
                IdentifyLog.Text += "Captura falhou ou foi cancelada. Template vazio.\r\n";
                EnableIdentifyButtons(true);
                return;
            }

            IdentifyLog.Text += "Template mesclado capturado. Enviando para a API...\r\n";

            string templateBase64 = result.finalTemplate;

            // 3. Montar a Requisição para a API
            var requestData = new CadastroBiometriaRequest
            {
                ColaboradorId = colaboradorSelecionado.Id,
                BiometriaTemplateBase64 = templateBase64
            };

            var jsonContent = JsonConvert.SerializeObject(requestData);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                // 4. Enviar para a API (Agora com o token de login!)
                var response = await httpClient.PostAsync(ApiBaseUrl + "/api/biometria/cadastrar", httpContent);

                if (response.IsSuccessStatusCode)
                {
                    IdentifyLog.Text += $"SUCESSO! Biometria cadastrada para {colaboradorSelecionado.Nome}.\r\n";

                    // Atualiza a lista para refletir a mudança
                    // (Irá recarregar a lista da API)
                    btnCarregarLista_Click(null, null);
                }
                else
                {
                    var errorMsg = await response.Content.ReadAsStringAsync();
                    // O erro "Unauthorized" não deve mais acontecer aqui
                    IdentifyLog.Text += $"ERRO API: {response.StatusCode} - {errorMsg}\r\n";
                }
            }
            catch (Exception ex)
            {
                IdentifyLog.Text += $"Falha na requisição API: {ex.Message}\r\n";
            }

            EnableIdentifyButtons(true);
        }

        #endregion

        #region configuration

        private static int FromTrack(int trackValue) => 500 * trackValue;
        private static int ToTrack(int value) => (int)(value / 500);

        private static string ToString(ConfigParam param)
        {
            switch (param)
            {
                case ConfigParam.MIN_VAR: return "Min Var";
                case ConfigParam.SIMILIARITY_THRESHOLD: return "Similarity Threshold";
                case ConfigParam.BUZZER_ON: return "Buzzer";
                case ConfigParam.TEMPLATE_FORMAT: return "Template Format";
                default: return "Unknown";
            }
        }

        private void SaveConfig(ConfigParam param, string value)
        {
            var ret = idbio.SetParameter(param, value);
            if (ret < RetCode.SUCCESS)
            {
                configurationLog.Text += "Error setting parameter \"" + ToString(param) + "\" with value \"" + value + "\": " + CIDBio.GetErrorMessage(ret) + "\r\n";
            }
            else
            {
                configurationLog.Text += ToString(param) + " set successfully\r\n";
            }
        }

        private bool LoadConfig(ConfigParam param, out string value)
        {
            value = "";
            var ret = idbio.GetParameter(param, out value);
            if (ret < RetCode.SUCCESS)
            {
                configurationLog.Text += "Error getting parameter \"" + ToString(param) + "\": " + CIDBio.GetErrorMessage(ret) + "\r\n";
                return false;
            }
            return true;
        }

        private void SaveAllConfig()
        {
            SaveConfig(ConfigParam.MIN_VAR, FromTrack(minVarTrack.Value).ToString());
            int threshold = chkAutomatic.Checked ? 0 : int.Parse(textBoxThreshold.Text);
            SaveConfig(ConfigParam.SIMILIARITY_THRESHOLD, threshold.ToString());
            SaveConfig(ConfigParam.BUZZER_ON, chkBuzzer.Checked ? "1" : "0");
        }

        private void LoadAllConfig()
        {
            if (LoadConfig(ConfigParam.MIN_VAR, out string minVar))
            {
                minVarTrack.Value = ToTrack(int.Parse(minVar));
            }

            if (LoadConfig(ConfigParam.SIMILIARITY_THRESHOLD, out string threshold))
            {
                textBoxThreshold.Text = threshold;
                chkAutomatic.Checked = threshold == "0";
            }

            if (LoadConfig(ConfigParam.BUZZER_ON, out string buzzer))
            {
                chkBuzzer.Checked = buzzer == "1";
            }
        }

        private void MinVarTrack_ValueChanged(object sender, EventArgs e)
        {
            SaveConfig(ConfigParam.MIN_VAR, FromTrack(minVarTrack.Value).ToString());
        }

        private void TextBoxThreshold_Leave(object sender, EventArgs e)
        {
            if (!int.TryParse(textBoxThreshold.Text, out int threshold))
            {
                textBoxThreshold.Text = "12300";
                return;
            }

            if (threshold == 0)
            {
                chkAutomatic.Checked = true;
                textBoxThreshold.Enabled = false;
            }
            else
            {
                chkAutomatic.Checked = false;
                textBoxThreshold.Enabled = true;
            }
            SaveConfig(ConfigParam.SIMILIARITY_THRESHOLD, threshold.ToString());
        }

        private void ChkAutomatic_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutomatic.Checked)
            {
                textBoxThreshold.Enabled = false;
                textBoxThreshold.Text = "0";
            }
            else
            {
                textBoxThreshold.Enabled = true;
                textBoxThreshold.Text = "12300";
            }
            SaveConfig(ConfigParam.SIMILIARITY_THRESHOLD, textBoxThreshold.Text);
        }

        private void ChkBuzzer_CheckedChanged(object sender, EventArgs e) => SaveConfig(ConfigParam.BUZZER_ON, chkBuzzer.Checked ? "1" : "0");

        private void BtnConfigDefault_Click(object sender, EventArgs e)
        {
            minVarTrack.Value = 2;
            textBoxThreshold.Text = "0";
            chkAutomatic.Checked = true;
            chkBuzzer.Checked = true;
            SaveAllConfig();
        }

        private void ConfigurationLog_TextChanged(object sender, EventArgs e)
        {
            // set the current caret position to the end
            configurationLog.SelectionStart = configurationLog.Text.Length;
            // scroll it automatically
            configurationLog.ScrollToCaret();
        }

        #endregion
    }
}