package ir.taranom.patient.preview;

import android.app.Activity;
import android.os.Bundle;
import android.graphics.Color;
import android.net.Uri;
import android.view.View;
import android.view.WindowInsets;
import android.webkit.CookieManager;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;
import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.Locale;

/** Bundled, permission-free demonstration. No authentication or backend bridge is fabricated. */
public final class MainActivity extends Activity {
    private static final String HOST = "appassets.androidplatform.net";
    private WebView browser;
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.WHITE);
        root.setLayoutDirection(View.LAYOUT_DIRECTION_RTL);
        // Android 15 targets draw edge to edge; keep app content outside system bars.
        root.setOnApplyWindowInsetsListener(new View.OnApplyWindowInsetsListener() {
            @Override public WindowInsets onApplyWindowInsets(View view, WindowInsets insets) {
                view.setPadding(insets.getSystemWindowInsetLeft(), insets.getSystemWindowInsetTop(),
                    insets.getSystemWindowInsetRight(), insets.getSystemWindowInsetBottom());
                return insets;
            }
        });
        TextView label = new TextView(this);
        label.setText("ترنم · نسخهٔ آزمایشی بدون اتصال به سرور");
        label.setTextColor(Color.rgb(1,51,101));
        label.setBackgroundColor(Color.rgb(238,243,248));
        label.setTextSize(12);
        label.setPadding(16,8,16,8);
        root.addView(label, new LinearLayout.LayoutParams(-1,-2));
        browser = new WebView(this);
        WebSettings settings = browser.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(false);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        settings.setCacheMode(WebSettings.LOAD_NO_CACHE);
        settings.setJavaScriptCanOpenWindowsAutomatically(false);
        settings.setSupportMultipleWindows(false);
        settings.setMediaPlaybackRequiresUserGesture(true);
        CookieManager.getInstance().setAcceptCookie(false);
        CookieManager.getInstance().setAcceptThirdPartyCookies(browser,false);
        WebView.setWebContentsDebuggingEnabled(false);
        browser.setWebViewClient(new WebViewClient() {
            @Override public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                return resource(request.getUrl(), request.getMethod());
            }
            @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                if (local(request.getUrl())) return false;
                Toast.makeText(MainActivity.this,"این پیوند در نسخهٔ آزمایشی باز نمی‌شود.",Toast.LENGTH_SHORT).show();
                return true;
            }
        });
        root.addView(browser, new LinearLayout.LayoutParams(-1,0,1));
        setContentView(root);
        // Start on the bundled home preview; sample labels remain visible in the web app.
        browser.loadUrl("https://" + HOST + "/patient-app");
    }
    private static boolean local(Uri url) {
        return "https".equals(url.getScheme()) && HOST.equals(url.getHost()) &&
            (url.getPort() == -1 || url.getPort() == 443) && url.getUserInfo() == null;
    }
    private WebResourceResponse resource(Uri url, String method) {
        if (!local(url) || !"GET".equals(method)) return error(403,"Forbidden");
        String path = url.getPath();
        if (path == null || path.contains("..") || path.contains("\\") || path.indexOf('\0') >= 0)
            return error(400,"Bad Request");
        if (path.startsWith("/api/")) return error(503,"Service Unavailable");
        String asset;
        if (path.equals("/patient-app") || path.equals("/patient-app/") ||
            path.startsWith("/patient-app/") || path.equals("/patient-portal") ||
            path.startsWith("/patient-portal/") || path.equals("/member") || path.startsWith("/member/"))
            asset = "web/index.html";
        else if (path.startsWith("/assets/") || path.equals("/manifest.webmanifest"))
            asset = "web" + path;
        else return error(404,"Not Found");
        try {
            InputStream stream = getAssets().open(asset);
            String mime = mime(asset);
            return new WebResourceResponse(mime,"UTF-8",200,"OK",
                Collections.singletonMap("Cache-Control","no-store"),stream);
        } catch (IOException missing) { return error(404,"Not Found"); }
    }
    private static WebResourceResponse error(int status,String reason) {
        byte[] content = "{\"code\":\"ANDROID_PREVIEW_DISCONNECTED\"}".getBytes(StandardCharsets.UTF_8);
        return new WebResourceResponse("application/json","UTF-8",status,reason,
            Collections.singletonMap("Cache-Control","no-store"),new ByteArrayInputStream(content));
    }
    private static String mime(String path) {
        String p=path.toLowerCase(Locale.ROOT);
        if(p.endsWith(".html"))return "text/html";
        if(p.endsWith(".js"))return "text/javascript";
        if(p.endsWith(".css"))return "text/css";
        if(p.endsWith(".svg"))return "image/svg+xml";
        if(p.endsWith(".png"))return "image/png";
        if(p.endsWith(".woff2"))return "font/woff2";
        if(p.endsWith(".webmanifest"))return "application/manifest+json";
        return "application/octet-stream";
    }
    @Override public void onBackPressed() {
        if(browser != null && browser.canGoBack()) browser.goBack(); else super.onBackPressed();
    }
    @Override protected void onDestroy() {
        if(browser != null) { browser.stopLoading(); browser.clearHistory(); browser.clearCache(true); browser.destroy(); browser=null; }
        super.onDestroy();
    }
}
