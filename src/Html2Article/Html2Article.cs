// Author: StanZhai 翟士丹（mail@zhaishidan.cn）. All rights reserved. See License.md in the project root for license information.
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StanSoft
{
    /// <summary>
    /// 文章正文数据模型
    /// </summary>
    public class Article
    {
        /// <summary>
        /// 文章标题
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// 正文文本
        /// </summary>
        public string Content { get; set; }
        /// <summary>
        /// 带标签正文
        /// </summary>
        public string ContentWithTags { get; set; }
        /// <summary>
        /// 文章发布时间
        /// </summary>
        public DateTime PublishDate { get; set; }
    }

    /// <summary>
    /// 解析Html页面的文章正文内容,基于文本密度的HTML正文提取类
    /// Date:   2012/12/30
    /// Update:
    ///     2013/7/10   优化文章头部分析算法，优化
    ///     2014/4/25   添加Html代码中注释过滤的正则
    ///     2016/...   支持按网络块(AddChunk)流式入库，配置改为实例级以支持并发批次
    ///
    /// </summary>
    public class Html2Article
    {
        #region 参数设置

        // 正则表达式过滤：正则表达式，要替换成的文本
        private static readonly string[][] Filters =
        {
            new[] { @"(?is)<script.*?>.*?</script>", "" },
            new[] { @"(?is)<style.*?>.*?</style>", "" },
            new[] { @"(?is)<!--.*?-->", "" },    // 过滤Html代码中的注释
            // 针对链接密集型的网站的处理，主要是门户类的网站，降低链接干扰
            new[] { @"(?is)</a>", "</a>\n"}
        };

        // 回车占位符，正文分析完成后统一还原
        private const string CrlfMarker = "[crlf]";

        // 确定文章正文头部时，向上查找，连续的空行到达_headEmptyLines，则停止查找
        private const int HeadEmptyLines = 2;
        // 用于确定文章结束的字符数
        private const int EndLimitCharCount = 20;

        /// <summary>
        /// 是否使用追加模式，默认为false
        /// 使用追加模式后，会将符合过滤条件的所有文本提取出来
        /// </summary>
        public bool AppendMode { get; set; }

        /// <summary>
        /// 按行分析的深度，默认为6
        /// </summary>
        public int Depth { get; set; }

        /// <summary>
        /// 字符限定数，当分析的文本数量达到限定数则认为进入正文内容
        /// 默认180个字符数
        /// </summary>
        public int LimitCount { get; set; }

        /// <summary>
        /// 文章来源地址，设置后会按该地址将带标签正文中的相对链接补全为绝对链接，
        /// 不设置则不处理链接。
        /// </summary>
        public string BaseUrl { get; set; }

        #endregion

        #region 流式(分块)入库状态

        // 已确认完整的Html前缀
        private readonly StringBuilder _buffer = new StringBuilder();
        // 末尾可能未闭合的标签/实体片段，等待下一个网络块拼接
        private string _pending = string.Empty;
        // Finish是否已被调用，Finish后再次AddChunk视为开启新一轮解析
        private bool _finished;
        private Article _lastResult;

        // 形如 &amp、&#39、& 这类尚未出现分号的实体片段
        private static readonly Regex IncompleteEntityRegex =
            new Regex(@"^&[#a-zA-Z0-9]*$", RegexOptions.Compiled);

        #endregion

        /// <summary>
        /// 使用默认参数创建解析器实例。每个实例拥有独立的配置与解析状态，
        /// 多个实例可在不同的并发批次中同时使用，互不影响。
        /// </summary>
        public Html2Article()
        {
            AppendMode = false;
            Depth = 6;
            LimitCount = 180;
        }

        /// <summary>
        /// 创建解析器实例，并指定文章来源地址用于补全相对链接。
        /// </summary>
        /// <param name="baseUrl">文章来源Url</param>
        public Html2Article(string baseUrl)
            : this()
        {
            BaseUrl = baseUrl;
        }

        /// <summary>
        /// 从给定的Html原始文本中获取正文信息（保留的原静态入口，使用默认配置）。
        /// </summary>
        /// <param name="html"></param>
        /// <returns></returns>
        public static Article GetArticle(string html)
        {
            return new Html2Article().Parse(html);
        }

        /// <summary>
        /// 从给定的Html原始文本中获取正文信息，并按baseUrl补全相对链接。
        /// </summary>
        /// <param name="html"></param>
        /// <param name="baseUrl">文章来源Url</param>
        /// <returns></returns>
        public static Article GetArticle(string html, string baseUrl)
        {
            return new Html2Article(baseUrl).Parse(html);
        }

        /// <summary>
        /// 追加一个网络块。块边界可能恰好切在标签或实体内部，
        /// 这里只做拼接与完整性判断，将未闭合的片段留到下一块处理，
        /// 不做任何实体解码或正文分析，保证实体只会被解码一次。
        /// </summary>
        /// <param name="chunk">网络块文本</param>
        public void AddChunk(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }
            if (_finished)
            {
                // 上一轮解析已经结束，开启新一轮
                _finished = false;
                _lastResult = null;
                _buffer.Length = 0;
                _pending = string.Empty;
            }

            string data = _pending + chunk;
            int cut = data.Length;

            // 末尾存在尚未出现 '>' 的 '<'，标签可能被网络块切断
            int lastLt = data.LastIndexOf('<');
            if (lastLt >= 0 && data.IndexOf('>', lastLt) < 0)
            {
                cut = Math.Min(cut, lastLt);
            }

            // 末尾存在尚未出现 ';' 的 '&'，实体可能被网络块切断
            int lastAmp = data.LastIndexOf('&');
            if (lastAmp >= 0 && data.IndexOf(';', lastAmp) < 0
                && IncompleteEntityRegex.IsMatch(data.Substring(lastAmp)))
            {
                cut = Math.Min(cut, lastAmp);
            }

            if (cut == data.Length)
            {
                _buffer.Append(data);
                _pending = string.Empty;
            }
            else
            {
                _buffer.Append(data, 0, cut);
                _pending = data.Substring(cut);
            }
        }

        /// <summary>
        /// 所有网络块追加完毕后调用，对拼接完整的Html执行一次解析，
        /// 标题、发布日期、纯文本正文与带标签正文均来自同一次解析结果。
        /// 重复调用返回同一次解析结果；再次调用AddChunk后视为新文档。
        /// </summary>
        /// <returns></returns>
        public Article Finish()
        {
            if (_finished)
            {
                return _lastResult;
            }

            string html = _buffer.ToString() + _pending;
            _buffer.Length = 0;
            _pending = string.Empty;
            _finished = true;
            _lastResult = Parse(html);
            return _lastResult;
        }

        /// <summary>
        /// 一次性解析完整Html文本（等价于AddChunk(html)后Finish）。
        /// </summary>
        /// <param name="html"></param>
        /// <returns></returns>
        public Article Parse(string html)
        {
            // 如果换行符的数量小于10，则认为html为压缩后的html
            // 由于处理算法是按照行进行处理，需要为html标签添加换行符，便于处理
            if (html == null)
            {
                html = string.Empty;
            }
            if (html.Count(c => c == '\n') < 10)
            {
                html = html.Replace(">", ">\n");
            }

            // 获取html，body标签内容；页面缺失body标签时退化为整页解析，避免正文为空
            string body;
            Match m = Regex.Match(html, @"(?is)<body.*?</body>");
            if (m.Success)
            {
                body = m.ToString();
            }
            else
            {
                body = html;
            }

            // 过滤样式，脚本等不相干标签
            foreach (var filter in Filters)
            {
                body = Regex.Replace(body, filter[0], filter[1]);
            }
            // 标签规整化处理，将标签属性格式化处理到同一行
            // 处理形如以下的标签：
            //  <a
            //   href='http://www.baidu.com'
            //   class='test'
            // 处理后为
            //  <a href='http://www.baidu.com' class='test'>
            body = Regex.Replace(body, @"(<[^<>]+)\s*\n\s*", FormatTag);

            string content;
            string contentWithTags;
            GetContent(body, out content, out contentWithTags);

            // 相对链接按传入的来源Url统一修正
            if (!string.IsNullOrEmpty(BaseUrl))
            {
                contentWithTags = UrlUtility.FixUrl(BaseUrl, contentWithTags);
            }

            Article article = new Article
            {
                Title = GetTitle(html),
                PublishDate = GetPublishDate(body),
                Content = content,
                ContentWithTags = contentWithTags
            };

            return article;
        }

        /// <summary>
        /// 格式化标签，剔除匹配标签中的回车符
        /// </summary>
        /// <param name="match"></param>
        /// <returns></returns>
        private static string FormatTag(Match match)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var ch in match.Value)
            {
                if (ch == '\r' || ch == '\n')
                {
                    continue;
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 获取时间
        /// </summary>
        /// <param name="html"></param>
        /// <returns></returns>
        private static string GetTitle(string html)
        {
            string titleFilter = @"<title>[\s\S]*?</title>";
            string h1Filter = @"<h1.*?>.*?</h1>";
            string clearFilter = @"<.*?>";

            string title = "";
            Match match = Regex.Match(html, titleFilter, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                title = Regex.Replace(match.Groups[0].Value, clearFilter, "");
            }

            // 正文的标题一般在h1中，比title中的标题更干净
            match = Regex.Match(html, h1Filter, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string h1 = Regex.Replace(match.Groups[0].Value, clearFilter, "");
                if (!String.IsNullOrEmpty(h1) && title.StartsWith(h1))
                {
                    title = h1;
                }
            }
            return title;
        }

        /// <summary>
        /// 获取文章发布日期
        /// </summary>
        /// <param name="html"></param>
        /// <returns></returns>
        private static DateTime GetPublishDate(string html)
        {
            // 过滤html标签，防止标签对日期提取产生影响
            string text = Regex.Replace(html, "(?is)<.*?>", "");
            Match match = Regex.Match(
                text,
                @"((\d{4}|\d{2})(\-|\/)\d{1,2}\3\d{1,2})(\s?\d{2}:\d{2})?|(\d{4}年\d{1,2}月\d{1,2}日)(\s?\d{2}:\d{2})?",
                RegexOptions.IgnoreCase);

            DateTime result = new DateTime(1900, 1, 1);
            if (match.Success)
            {
                try
                {
                    string dateStr = "";
                    for (int i = 0; i < match.Groups.Count; i++)
                    {
                        dateStr = match.Groups[i].Value;
                        if (!String.IsNullOrEmpty(dateStr))
                        {
                            break;
                        }
                    }
                    // 对中文日期的处理
                    if (dateStr.Contains("年"))
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (var ch in dateStr)
                        {
                            if (ch == '年' || ch == '月')
                            {
                                sb.Append("/");
                                continue;
                            }
                            if (ch == '日')
                            {
                                sb.Append(' ');
                                continue;
                            }
                            sb.Append(ch);
                        }
                        dateStr = sb.ToString();
                    }
                    result = Convert.ToDateTime(dateStr);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
                if (result.Year < 1900)
                {
                    result = new DateTime(1900, 1, 1);
                }
            }
            return result;
        }

        /// <summary>
        /// 从body标签文本中分析正文内容
        /// </summary>
        /// <param name="bodyText">只过滤了script和style标签的body文本内容</param>
        /// <param name="content">返回文本正文，不包含标签</param>
        /// <param name="contentWithTags">返回文本正文包含标签</param>
        private void GetContent(string bodyText, out string content, out string contentWithTags)
        {
            string[] orgLines = null;   // 保存原始内容，按行存储
            string[] lines = null;      // 保存干净的文本内容，不包含标签

            orgLines = bodyText.Split('\n');
            lines = new string[orgLines.Length];
            // 去除每行的空白字符,剔除标签
            for (int i = 0; i < orgLines.Length; i++)
            {
                string lineInfo = orgLines[i];
                // 处理回车，使用[crlf]做为回车标记符，最后统一处理；兼容<br>、<br/>、<br />等写法
                lineInfo = Regex.Replace(lineInfo, @"(?is)</p>|<br\b[^>]*>", CrlfMarker);
                lines[i] = Regex.Replace(lineInfo, "(?is)<.*?>", "").Trim();
            }

            StringBuilder sb = new StringBuilder();
            StringBuilder orgSb = new StringBuilder();

            int preTextLen = 0;         // 记录上一次统计的字符数量
            int startPos = -1;          // 记录文章正文的起始位置
            for (int i = 0; i < lines.Length - Depth; i++)
            {
                int len = 0;
                for (int j = 0; j < Depth; j++)
                {
                    len += lines[i + j].Length;
                }

                if (startPos == -1)     // 还没有找到文章起始位置，需要判断起始位置
                {
                    if (preTextLen > LimitCount && len > 0)    // 如果上次查找的文本数量超过了限定字数，且当前行数字符数不为0，则认为是开始位置
                    {
                        // 查找文章起始位置, 如果向上查找，发现2行连续的空行则认为是头部
                        int emptyCount = 0;
                        for (int j = i - 1; j > 0; j--)
                        {
                            if (String.IsNullOrEmpty(lines[j]))
                            {
                                emptyCount++;
                            }
                            else
                            {
                                emptyCount = 0;
                            }
                            if (emptyCount == HeadEmptyLines)
                            {
                                startPos = j + HeadEmptyLines;
                                break;
                            }
                        }
                        // 如果没有定位到文章头，则以当前查找位置作为文章头
                        if (startPos == -1)
                        {
                            startPos = i;
                        }
                        // 填充发现的文章起始部分
                        for (int j = startPos; j <= i; j++)
                        {
                            sb.Append(lines[j]);
                            orgSb.Append(orgLines[j]);
                        }
                    }
                }
                else
                {
                    //if (len == 0 && preTextLen == 0)    // 当前长度为0，且上一个长度也为0，则认为已经结束
                    if (len <= EndLimitCharCount && preTextLen < EndLimitCharCount)    // 当前长度为0，且上一个长度也为0，则认为已经结束
                    {
                        if (!AppendMode)
                        {
                            break;
                        }
                        startPos = -1;
                    }
                    sb.Append(lines[i]);
                    orgSb.Append(orgLines[i]);
                }
                preTextLen = len;
            }

            // 实体统一解码一次（此时占位符仍为[crlf]，不会被实体编码污染），
            // 再还原回车符；形如&#91;crlf&#93;的编码内容解码后也能被正确还原
            string result = System.Web.HttpUtility.HtmlDecode(sb.ToString());
            content = result.Replace(CrlfMarker, Environment.NewLine);
            // 输出带标签文本
            contentWithTags = orgSb.ToString();
        }
    }
}
