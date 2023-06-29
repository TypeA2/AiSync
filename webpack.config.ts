import * as webpack from "webpack";
import * as externals from "webpack-node-externals";
import * as HtmlWebpackPlugin from "html-webpack-plugin";
import * as MiniCssExtractPlugin from "mini-css-extract-plugin";
import * as CopyPlugin from "copy-webpack-plugin";
import * as autoprefixer from "autoprefixer";
import { CleanWebpackPlugin } from "clean-webpack-plugin";

import * as path from "path";
import { stringify } from "uuid";

const mode = process.env.NODE_ENV?.startsWith("prod") ? "production" : "development";

function create_client_config(entry: string, options?: HtmlWebpackPlugin.Options | undefined): webpack.Configuration {
    return {
        mode: mode,
        devtool: "source-map",
        watch: true,
        entry: entry,
        output: {
            path: path.resolve(__dirname, "dist", "client"),
            filename: `js/${path.parse(entry).name}.[name].js`,
            clean: false
        },
        module: {
            rules: [
                {
                    test: /\.ts$/,
                    use: "ts-loader",
                    exclude: /node_modules/
                },
                {
                    test: /\.s?[ac]ss$/i,
                    use: [
                        MiniCssExtractPlugin.loader,
                        "css-loader",
                        {
                            loader: "postcss-loader",
                            options: {
                                postcssOptions: {
                                    plugins: () => [ autoprefixer ]
                                }
                            }
                        },
                        "sass-loader",
                    ]
                }
            ]
        },
        resolve: {
            extensions: [".ts", ".js"]
        },
        plugins: [
            new MiniCssExtractPlugin({
                filename: `css/${path.parse(entry).name}.[id].css`,
            }),
            new HtmlWebpackPlugin(options),
            new webpack.DefinePlugin({
                "process.env.IS_NODE": JSON.stringify(false)
            })
        ],
        optimization: {
            runtimeChunk: "multiple",
            moduleIds: "deterministic",
            splitChunks: {
                cacheGroups: {
                    vendor: {
                        test: /[\\/]node_modules[\\/]/,
                        name: "vendors",
                        chunks: "all"
                    }
                }
            }
        }
    }
}

const pages = [
    create_client_config("./client/login.ts", {
        filename: "html/login.html",
        template: "client/html/login.html",
        favicon: "./favicon.ico",
        title: "AiSync - Login",
        publicPath: "/"
    }),
    create_client_config("./client/watch.ts", {
        filename: "html/watch.html",
        template: "client/html/watch.html",
        favicon: "./favicon.ico",
        title: "AiSync - Watch",
        publicPath: "/"
    }),
    create_client_config("./client/admin.ts", {
        filename: "html/admin.html",
        template: "client/html/admin.html",
        favicon: "./favicon.ico",
        title: "AiSync - Admin",
        publicPath: "/"
    })
];

const server_config: webpack.Configuration = {
    target: "node",
    mode: mode,
    watch: true,
    devtool: "source-map",
    entry: "./server/index.ts",
    output: {
        path: path.resolve(__dirname, "dist", "server"),
        filename: "ai-sync.js"
    },
    plugins: [
        new webpack.DefinePlugin({
            "process.env.IS_NODE": JSON.stringify(true)
        })
    ],
    module: {
        rules: [
            {
                test: /\.ts$/,
                use: "ts-loader",
            }
        ]
    },
    resolve: {
        extensions: [".ts", ".js"]
    },
    externals: externals()
}

const cleanup: webpack.Configuration = {
    mode: mode,
    entry: "./stub.js",
    output: {
        path: path.resolve(__dirname, "dist"),
    },
    plugins: [
        new CleanWebpackPlugin({
            cleanOnceBeforeBuildPatterns: [
                path.resolve(__dirname, "dist")
            ]
        })
    ]
};

const copy_assets: webpack.Configuration = {
    mode: mode,
    entry: "./stub.js",
    plugins: [
        new CopyPlugin({
            patterns: [
                { from: "client/assets", to: "client/assets" }
            ]
        })
    ]
};

export default [ cleanup, copy_assets, server_config, ...pages ];
