﻿namespace WebViewControl {

    public interface IViewModule {

        string JavascriptSource { get; }

        string JavascriptName { get; }

        object CreateNativeObject();
    }
}
