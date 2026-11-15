using System;
using System.Text;

namespace StanSoft.Tests
{
    /// <summary>
    /// 测试用Html素材，覆盖标题、发布日期、实体、嵌套标签、br的各种写法、相对链接等场景。
    /// </summary>
    internal static class TestFixtures
    {
        /// <summary>
        /// 一篇完整、带body的文章Html，正文足够长以触发文本密度算法。
        /// 注意：提取算法不会分析最后Depth行，因此断言涉及的段落之后仍需保留足够多的正文行。
        /// </summary>
        public const string FullArticle =
            "<!DOCTYPE html>\n" +
            "<html>\n" +
            "<head>\n" +
            "<meta charset=\"utf-8\" />\n" +
            "<title>深度解析流式正文提取技术的设计与实现之路</title>\n" +
            "<script type=\"text/javascript\">var ga = 'tracker';</script>\n" +
            "<style>.ad { display: none; }</style>\n" +
            "</head>\n" +
            "<body>\n" +
            "<div class=\"nav\">首页 导航 关于</div>\n" +
            "<h1>深度解析流式正文提取技术的设计与实现之路</h1>\n" +
            "<div class=\"meta\">发布于 2024-05-18 10:30</div>\n" +
            "<div id=\"article\">\n" +
            "<p>这是第一段正文，包含普通文本与标点符号，用于验证基础的文本密度统计逻辑是否能够正确识别正文区域的起始位置。</p>\n" +
            "<p>第二段正文里包含实体：Tom &amp; Jerry 是经典动画，&lt;script&gt; 标签应被视为文本，A&amp;B 与 &quot;引号&quot; 以及 &#39;单引号&#39; 都只应解码一次。</p>\n" +
            "<p>第三段包含换行：第一行<br>第二行<br/>第三行<br />第四行，四种写法都应当转换为真正的换行而不是残留占位符。</p>\n" +
            "<p>第四段是嵌套标签，外层段落中包含<strong>加粗文本</strong>、<em>斜体文本</em>以及<a href=\"detail/1.html\">相对链接</a>与<a href=\"https://example.com/cdn.png\">绝对链接保持不变</a>。</p>\n" +
            "<p>第五段包含图片：<img src=\"images/pic.png\" alt=\"图片说明\" />，图片的相对地址同样需要被修正为绝对地址。</p>\n" +
            "<p>第六段验证双重编码不会被二次解码：&amp;amp; 应保留为 &amp; 而不是变成单个 & 符号，这是单次解码的关键判据。</p>\n" +
            "<p>第七段继续补充足够多的中文正文内容，确保窗口深度内的字符数量稳定超过限定字数，算法不会提前误判正文已经结束而丢失后面的段落内容。</p>\n" +
            "<p>第八段收尾段落，仍然保持较长的篇幅，嵌套的<span style=\"color:red\">行内元素</span>在纯文本中只保留文字，在带标签正文中保留结构。</p>\n" +
            "<p>第九段是补充正文，持续提供稳定的文本密度，避免末尾段落落入算法不分析的尾部窗口而丢失，保证前面所有断言目标都处于可提取区间之内。</p>\n" +
            "<p>第十段继续铺陈正文内容，文字长度保持在每行数十个汉字以上，使滑动窗口的统计值始终高于结束阈值，提取过程不会在正文中间中断。</p>\n" +
            "<p>第十一段再补充一段较长的中文叙述，涵盖标点、数字一二三与英文字母abc混合的场景，确保行统计在各种内容形态下都表现稳定可靠。</p>\n" +
            "<p>第十二段依旧是充足的正文填充内容，用于把页面尾部的版权信息与空白行推到最后一个滑动窗口之外，使正文区域的判断不受页脚干扰。</p>\n" +
            "<p>第十三段保持同样的篇幅与密度，作为正文与页脚之间的缓冲段落，确保前面的关键段落不会因为行号靠近尾部而被算法的循环边界排除掉。</p>\n" +
            "<p>第十四段是最后一段补充正文，仍然包含足够多的汉字字符，末尾再由独立的页脚区块收尾，整体结构接近真实资讯网站的文章页面布局。</p>\n" +
            "</div>\n" +
            "<div class=\"footer\">版权所有 &copy; 2024</div>\n" +
            "</body>\n" +
            "</html>";

        /// <summary>
        /// 与FullArticle内容相同，但去掉body标签（离线片段常见形态），
        /// 同时压缩为几乎不含换行的文本，模拟压缩后的Html。
        /// </summary>
        public static readonly string NoBodyArticle = FullArticle
            .Replace("<!DOCTYPE html>\n", "")
            .Replace("<body>\n", "")
            .Replace("\n</body>", "")
            .Replace("\n", "");

        /// <summary>
        /// 较长的页面，每个段落都带批次标识，通过实例级LimitCount/Depth/AppendMode配置
        /// 验证并发批次之间的配置隔离。段落数与行密度保证Depth=9、LimitCount=300时仍能提取到正文。
        /// </summary>
        public static string ShortArticle(int marker)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<html><head><title>批次页面");
            sb.Append(marker);
            sb.Append("</title></head><body>");
            // 顶部的短导航
            sb.Append("<div>导航栏目首页关于联系信息</div>");
            sb.Append("<div class=\"content\">");
            for (int p = 0; p < 14; p++)
            {
                sb.Append("<p>批次");
                sb.Append(marker);
                sb.Append("第");
                sb.Append(p + 1);
                sb.Append("段正文，带有明确的批次标识文字，段落保持充足的中文篇幅以支撑滑动窗口的文本密度统计，");
                sb.Append("确保在较大的分析深度与较高的字符限定数配置下，窗口内仍然累积足够多的有效字符，");
                sb.Append("即使分析深度只有三行，连续段落组成的窗口也能稳定越过字符限定阈值，");
                sb.Append("解析器能够稳定进入正文区域并提取到批次");
                sb.Append(marker);
                sb.Append("的标识内容，不同实例之间各自独立完成解析互不干扰。</p>");
            }
            sb.Append("</div>");
            sb.Append("<div class=\"footer\">页脚版权信息批次");
            sb.Append(marker);
            sb.Append("</div>");
            sb.Append("</body></html>");
            return sb.ToString();
        }
    }
}
