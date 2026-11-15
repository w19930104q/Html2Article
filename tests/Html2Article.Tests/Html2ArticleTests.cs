using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using StanSoft;

namespace StanSoft.Tests
{
    [TestFixture]
    public class Html2ArticleTests
    {
        /// <summary>
        /// 将html按切点切分后逐块喂给流式解析器，还原切点处的拼接行为。
        /// </summary>
        private static Article FeedInChunks(string html, int cut, string baseUrl = null)
        {
            Html2Article parser = new Html2Article(baseUrl);
            for (int pos = 0; pos < html.Length; pos += cut)
            {
                int len = Math.Min(cut, html.Length - pos);
                parser.AddChunk(html.Substring(pos, len));
            }
            return parser.Finish();
        }

        private static void AssertArticleEqual(Article expected, Article actual)
        {
            Assert.AreEqual(expected.Title, actual.Title, "Title");
            Assert.AreEqual(expected.PublishDate, actual.PublishDate, "PublishDate");
            Assert.AreEqual(expected.Content, actual.Content, "Content");
            Assert.AreEqual(expected.ContentWithTags, actual.ContentWithTags, "ContentWithTags");
        }

        [Test]
        public void ChunkedParsing_EqualsOneShotParsing_AtEveryCutPoint()
        {
            string html = TestFixtures.FullArticle;
            Article baseline = new Html2Article().Parse(html);

            // 覆盖单字符、小块、大块以及文档总长等各种分段切点，
            // 切点必然落在标签内部、实体内部以及普通文本中间
            int[] cuts = { 1, 2, 3, 5, 7, 13, 31, 64, 127, 256, 512, 1024, html.Length, html.Length + 10 };
            foreach (int cut in cuts)
            {
                Article chunked = FeedInChunks(html, cut);
                AssertArticleEqual(baseline, chunked);
            }

            // 基线本身必须确实提取到了正文，否则相等比较没有意义
            StringAssert.Contains("流式正文提取", baseline.Content);
            Assert.IsNotEmpty(baseline.ContentWithTags);
        }

        [Test]
        public void ChunkedParsing_PreservesTagSplitAcrossChunks()
        {
            // 切点直接落在 <strong> 标签内部：<st | rong>
            string html = TestFixtures.FullArticle;
            int idx = html.IndexOf("<strong>");
            Assert.Greater(idx, 0);

            Html2Article parser = new Html2Article();
            parser.AddChunk(html.Substring(0, idx + 3));   // 以 "<st" 结尾，标签未闭合
            parser.AddChunk(html.Substring(idx + 3));       // 剩余部分以 "rong>" 开头
            Article article = parser.Finish();

            Article baseline = new Html2Article().Parse(html);
            AssertArticleEqual(baseline, article);
            // 未闭合标签被跨块拼回，纯文本中保留文字且不残留尖括号碎片
            StringAssert.Contains("加粗文本", article.Content);
            StringAssert.DoesNotContain("<st", article.Content);
        }

        [Test]
        public void ChunkedParsing_PreservesEntitySplitAcrossChunks()
        {
            // 在FullArticle上精确构造被网络块切断的实体：
            //   &am | p;   （&amp; 从中间切开）
            //   & | #39;   （&#39; 从 & 之后切开）
            string html = TestFixtures.FullArticle;
            int ampIdx = html.IndexOf("&amp;");
            int hashIdx = html.IndexOf("&#39;");
            Assert.Greater(ampIdx, 0);
            Assert.Greater(hashIdx, ampIdx);

            Html2Article parser = new Html2Article();
            parser.AddChunk(html.Substring(0, ampIdx + 3));    // 以 "&am" 结尾，实体未闭合
            parser.AddChunk(html.Substring(ampIdx + 3, (hashIdx + 1) - (ampIdx + 3))); // 以 "&" 结尾
            parser.AddChunk(html.Substring(hashIdx + 1));      // 剩余部分以 "#39;" 开头
            Article article = parser.Finish();

            // 与一次性解析完全一致，说明实体被跨块拼回后才统一解码
            Article baseline = new Html2Article().Parse(html);
            AssertArticleEqual(baseline, article);
            StringAssert.Contains("Tom & Jerry", article.Content);
            StringAssert.Contains("'单引号'", article.Content);
            // 不允许残留未解码的实体碎片（被切开的&amp;必须拼回后解码）
            StringAssert.DoesNotContain("&#39;", article.Content);
            StringAssert.DoesNotContain("Tom &am", article.Content);
        }

        [Test]
        public void Entities_AreDecodedExactlyOnce()
        {
            Article article = new Html2Article().Parse(TestFixtures.FullArticle);

            // 普通实体解码为字符
            StringAssert.Contains("Tom & Jerry", article.Content);
            StringAssert.Contains("\"引号\"", article.Content);
            StringAssert.Contains("'单引号'", article.Content);
            // 双重编码只解码一层，证明没有发生二次解码
            StringAssert.Contains("&amp;", article.Content);
            StringAssert.DoesNotContain("&amp;amp;", article.Content);
        }

        [Test]
        public void BrPlaceholders_DoNotPolluteContent()
        {
            Article article = new Html2Article().Parse(TestFixtures.FullArticle);

            // <br>、<br/>、<br /> 都转为换行，占位符不得残留
            StringAssert.DoesNotContain("[crlf]", article.Content);
            StringAssert.Contains("第一行" + Environment.NewLine + "第二行", article.Content);
            StringAssert.Contains("第二行" + Environment.NewLine + "第三行", article.Content);
            StringAssert.Contains("第三行" + Environment.NewLine + "第四行", article.Content);
            StringAssert.DoesNotContain("<br", article.Content);
        }

        [Test]
        public void EncodedCrlfMarker_IsDecodedBeforeMarkerReplacement()
        {
            // 源页面中以实体形式出现的占位符：&#91;crlf&#93; 解码后即 [crlf]，
            // 必须先解码再统一替换，否则旧实现会把字面 [crlf] 留在正文里
            string html = TestFixtures.FullArticle.Replace(
                "四种写法都应当转换为真正的换行而不是残留占位符。",
                "另外验证编码占位&#91;crlf&#93;不会污染输出结果。");
            Article article = new Html2Article().Parse(html);

            StringAssert.DoesNotContain("[crlf]", article.Content);
            StringAssert.DoesNotContain("&#91;", article.Content);
            StringAssert.Contains("编码占位" + Environment.NewLine + "不会污染输出结果", article.Content);
        }

        [Test]
        public void NestedTags_TextAndTaggedContentComeFromSameParse()
        {
            Article article = new Html2Article().Parse(TestFixtures.FullArticle);

            // 纯文本保留文字、剔除标签
            StringAssert.Contains("加粗文本", article.Content);
            StringAssert.Contains("斜体文本", article.Content);
            StringAssert.DoesNotContain("<strong>", article.Content);
            StringAssert.DoesNotContain("<em>", article.Content);

            // 带标签正文保留嵌套结构
            StringAssert.Contains("<strong>加粗文本</strong>", article.ContentWithTags);
            StringAssert.Contains("<em>斜体文本</em>", article.ContentWithTags);
            StringAssert.Contains("<span style=\"color:red\">行内元素</span>", article.ContentWithTags);
        }

        [Test]
        public void NoBodyPage_IsParsedInsteadOfReturningEmpty()
        {
            Article article = new Html2Article().Parse(TestFixtures.NoBodyArticle);

            StringAssert.Contains("流式正文提取", article.Content);
            StringAssert.Contains("加粗文本", article.Content);
            StringAssert.Contains("<strong>加粗文本</strong>", article.ContentWithTags);
            Assert.IsNotEmpty(article.Title);
        }

        [Test]
        public void RelativeLinks_AreFixedByBaseUrl()
        {
            const string baseUrl = "https://www.stan.com/news/2024/article.html";
            Article article = new Html2Article(baseUrl).Parse(TestFixtures.FullArticle);

            StringAssert.Contains("href=\"https://www.stan.com/news/2024/detail/1.html\"", article.ContentWithTags);
            StringAssert.Contains("src=\"https://www.stan.com/news/2024/images/pic.png\"", article.ContentWithTags);
            // 绝对链接保持原样
            StringAssert.Contains("href=\"https://example.com/cdn.png\"", article.ContentWithTags);

            // 不设置BaseUrl时不改动链接
            Article raw = new Html2Article().Parse(TestFixtures.FullArticle);
            StringAssert.Contains("href=\"detail/1.html\"", raw.ContentWithTags);
            StringAssert.Contains("src=\"images/pic.png\"", raw.ContentWithTags);
        }

        [Test]
        public void RelativeLinks_AreFixedInChunkedMode()
        {
            const string baseUrl = "https://www.stan.com/news/2024/article.html";
            Article oneShot = new Html2Article(baseUrl).Parse(TestFixtures.FullArticle);
            Article chunked = FeedInChunks(TestFixtures.FullArticle, 37, baseUrl);

            AssertArticleEqual(oneShot, chunked);
            StringAssert.Contains("https://www.stan.com/news/2024/detail/1.html", chunked.ContentWithTags);
        }

        [Test]
        public void StaticGetArticle_KeepsOriginalEntryBehavior()
        {
            Article viaInstance = new Html2Article().Parse(TestFixtures.FullArticle);
            Article viaStatic = Html2Article.GetArticle(TestFixtures.FullArticle);

            AssertArticleEqual(viaInstance, viaStatic);
            Assert.AreEqual("深度解析流式正文提取技术的设计与实现之路", viaStatic.Title);
            Assert.AreEqual(new DateTime(2024, 5, 18, 10, 30, 0), viaStatic.PublishDate);

            // 静态入口对无body页面同样生效
            Article noBody = Html2Article.GetArticle(TestFixtures.NoBodyArticle);
            StringAssert.Contains("流式正文提取", noBody.Content);
        }

        [Test]
        public void InstanceConfig_DoesNotLeakAcrossConcurrentBatches()
        {
            const int batches = 24;
            ConcurrentBag<string> failures = new ConcurrentBag<string>();

            Parallel.For(0, batches, i =>
            {
                // 每个批次使用独立的解析器实例与独立配置
                Html2Article parser = new Html2Article
                {
                    LimitCount = (i % 2 == 0) ? 30 : 300,
                    Depth = (i % 3 == 0) ? 3 : 9,
                    AppendMode = (i % 4 == 0)
                };

                // 同批次内再用一个默认实例做对照
                Html2Article standard = new Html2Article();

                string html = TestFixtures.ShortArticle(i);
                Article custom = parser.Parse(html);
                Article baseline = standard.Parse(html);
                Article expectedCustom = new Html2Article
                {
                    LimitCount = parser.LimitCount,
                    Depth = parser.Depth,
                    AppendMode = parser.AppendMode
                }.Parse(html);

                if (custom.Title != baseline.Title || !custom.Title.Contains(i.ToString()))
                {
                    failures.Add("batch " + i + " title mismatch: " + custom.Title);
                }
                if (custom.Content != expectedCustom.Content)
                {
                    failures.Add("batch " + i + " content differs from same-config serial result");
                }
                if (!custom.Content.Contains("批次" + i))
                {
                    failures.Add("batch " + i + " lost its marker text");
                }
            });

            Assert.IsEmpty(failures, "并发批次出现配置串扰或结果错误: " + string.Join(" | ", failures));
        }

        [Test]
        public void Finish_IsIdempotent_AndInstanceCanBeReused()
        {
            Html2Article parser = new Html2Article();
            parser.AddChunk(TestFixtures.FullArticle);
            Article first = parser.Finish();
            Article second = parser.Finish();

            AssertArticleEqual(first, second);

            // Finish之后再次AddChunk开启新一轮解析，旧状态不会污染新文档
            parser.AddChunk(TestFixtures.ShortArticle(7));
            Article reused = parser.Finish();
            Assert.IsTrue(reused.Title.Contains("7"));
            StringAssert.DoesNotContain("流式正文提取", reused.Content);
        }

        [Test]
        public void EmptyAndNullInputs_ReturnEmptyArticleInsteadOfThrowing()
        {
            Assert.DoesNotThrow(() =>
            {
                Article a = Html2Article.GetArticle(null);
                Assert.AreEqual("", a.Title);
                Assert.AreEqual("", a.Content);
                Assert.AreEqual("", a.ContentWithTags);
                Assert.AreEqual(new DateTime(1900, 1, 1), a.PublishDate);

                Html2Article parser = new Html2Article();
                parser.AddChunk(null);
                parser.AddChunk("");
                Article b = parser.Finish();
                Assert.AreEqual("", b.Content);
            });
        }
    }
}
