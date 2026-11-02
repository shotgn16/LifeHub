plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// Release signing comes from environment variables (GitHub secrets in CI, or your shell):
// LIFEHUB_KEYSTORE (path), LIFEHUB_KEYSTORE_PASSWORD, LIFEHUB_KEY_ALIAS, LIFEHUB_KEY_PASSWORD.
// Without them, release builds are signed with the debug key (fine for trying it out).
val keystorePath: String? = System.getenv("LIFEHUB_KEYSTORE")

android {
    namespace = "io.github.shotgn16.lifehub"
    compileSdk = 35

    defaultConfig {
        applicationId = "io.github.shotgn16.lifehub"
        minSdk = 26
        targetSdk = 35
        versionCode = (project.findProperty("versionCode") as String?)?.toInt() ?: 1
        versionName = (project.findProperty("versionName") as String?) ?: "1.0.0"
    }

    signingConfigs {
        create("release") {
            if (keystorePath != null) {
                storeFile = file(keystorePath)
                storePassword = System.getenv("LIFEHUB_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("LIFEHUB_KEY_ALIAS")
                keyPassword = System.getenv("LIFEHUB_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = if (keystorePath != null) signingConfigs.getByName("release") else signingConfigs.getByName("debug")
        }
    }

    // The offline screen is shared with the iPhone app (mobile/shared/offline.html).
    sourceSets {
        getByName("main") { assets.srcDirs("src/main/assets", "../../shared") }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
}

dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.work:work-runtime-ktx:2.9.1")
}
