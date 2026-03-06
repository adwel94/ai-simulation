from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    google_api_key: str = ""
    model_name: str = "gemini-3.1-flash-preview"
    unity_server_url: str = "http://localhost:8765"
    max_steps: int = 50
    data_dir: str = "data"

    model_config = {"env_file": ".env", "env_file_encoding": "utf-8"}


settings = Settings()
